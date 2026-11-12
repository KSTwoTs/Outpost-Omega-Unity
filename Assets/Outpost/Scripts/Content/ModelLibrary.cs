using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;
using Newtonsoft.Json;

namespace Outpost.Content
{
    /// <summary>A loaded mesh with the materials for its submeshes.</summary>
    public class ModelMesh
    {
        public Mesh mesh;
        public Material[] materials;
        public bool Alive => mesh != null;      // false once Unity has destroyed it (after leaving play mode)
    }

    /// <summary>A piece of a model that moves on its own: a turret that turns, a rotor that spins.</summary>
    [Serializable]
    public class ModelPart
    {
        public string name;        // child object name
        public string model;       // the part's own model in the pak
        public string kind;        // "turret" (aims) or "spin"
        public float[] pivot;      // where it sits on the body, model space

        public Vector3 Pivot => pivot != null && pivot.Length == 3 ? new Vector3(pivot[0], pivot[1], pivot[2]) : Vector3.zero;
    }

    /// <summary>
    /// Loads the meshes exported by tools/export_unity.py in the Python project and builds URP Lit
    /// materials from their colour and kind. Two paks are read from StreamingAssets/Outpost:
    /// models.pak (every model as one solid mesh) and models_anim.pak, which replaces units and
    /// turrets with versions that have poses (walk1, attack) and separate moving parts.
    /// </summary>
    public static class ModelLibrary
    {
        [Serializable]
        class MatInfo
        {
            public string name;
            public float[] color;
            public string kind;
            public float metallic;
            public float smoothness;
            public float emission;
        }

        [Serializable]
        class ModelInfo
        {
            public string name;
            public int vertices;
            public int[] submeshes;
            public MatInfo[] materials;
            public float[] bounds_min;
            public float[] bounds_max;
            public long offset;
            public long length;
            public Dictionary<string, string> poses;      // pose name -> model name
            public ModelPart[] parts;
            [JsonIgnore] public string file;              // which .bin holds it
        }

        static readonly Dictionary<string, ModelMesh> cache = new Dictionary<string, ModelMesh>();
        static readonly Dictionary<string, Material> matCache = new Dictionary<string, Material>();
        static readonly string[] Paks = { "models.pak", "models_anim.pak" };     // later ones win

        public static string Root => Path.Combine(Application.streamingAssetsPath, "Outpost");

        static Dictionary<string, ModelInfo> index;

        static Dictionary<string, ModelInfo> Index
        {
            get
            {
                if (index != null) return index;
                index = new Dictionary<string, ModelInfo>();
                foreach (var pak in Paks)
                {
                    string json = Path.Combine(Root, pak + ".json");
                    if (!File.Exists(json)) continue;
                    var part = JsonConvert.DeserializeObject<Dictionary<string, ModelInfo>>(File.ReadAllText(json));
                    foreach (var kv in part)
                    {
                        kv.Value.file = Path.Combine(Root, pak + ".bin");
                        index[kv.Key] = kv.Value;
                    }
                }
                return index;
            }
        }

        /// <summary>
        /// Play mode can start without reloading scripts, and Unity destroys the meshes and materials
        /// made during the last session: start every session with empty caches.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetOnPlay() => Reload();

        public static bool Has(string name) => Index.ContainsKey(name);

        /// <summary>Forget everything loaded (after re-exporting models).</summary>
        public static void Reload()
        {
            index = null;
            cache.Clear();
            matCache.Clear();
        }

        /// <summary>The model's mesh and materials, or null if it wasn't exported.</summary>
        public static ModelMesh Get(string name) => name == null ? null : Load(name);

        /// <summary>The model that shows this one in a pose ("walk1", "attack"), or null if it has no such pose.</summary>
        public static string PoseName(string name, string pose)
        {
            if (name == null || !Index.TryGetValue(name, out var info) || info.poses == null) return null;
            return info.poses.TryGetValue(pose, out var v) ? v : null;
        }

        static readonly ModelPart[] NoParts = new ModelPart[0];

        public static ModelPart[] Parts(string name) =>
            name != null && Index.TryGetValue(name, out var info) && info.parts != null ? info.parts : NoParts;

        /// <summary>
        /// A new GameObject showing the model, or null if the model wasn't exported. Moving parts
        /// become child objects named after the part, placed at their pivots.
        /// </summary>
        public static GameObject Spawn(string name, Transform parent = null)
        {
            var e = Load(name);
            if (e == null) return null;
            var go = Piece(name, e, parent);
            foreach (var p in Parts(name))
            {
                var pe = Load(p.model);
                if (pe == null) continue;
                Piece(p.name, pe, go.transform).transform.localPosition = p.Pivot;
            }
            return go;
        }

        static GameObject Piece(string name, ModelMesh e, Transform parent)
        {
            var go = new GameObject(name);
            if (parent != null) go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = e.mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterials = e.materials;
            mr.shadowCastingMode = ShadowCastingMode.On;
            return go;
        }

        static ModelMesh Load(string name)
        {
            if (cache.TryGetValue(name, out var hit) && (hit == null || hit.Alive)) return hit;
            if (!Index.TryGetValue(name, out var info))
            {
                Debug.LogWarning($"[Outpost] model '{name}' not exported");
                cache[name] = null;
                return null;
            }
            var bytes = new byte[info.length];
            using (var fs = File.OpenRead(info.file))
            {
                fs.Seek(info.offset, SeekOrigin.Begin);
                int read = 0;
                while (read < bytes.Length)
                {
                    int got = fs.Read(bytes, read, bytes.Length - read);
                    if (got <= 0) break;
                    read += got;
                }
            }
            int n = info.vertices;
            var pos = new Vector3[n];
            var nrm = new Vector3[n];
            int o = 0;
            for (int i = 0; i < n; i++, o += 12)
                pos[i] = new Vector3(BitConverter.ToSingle(bytes, o), BitConverter.ToSingle(bytes, o + 4), BitConverter.ToSingle(bytes, o + 8));
            for (int i = 0; i < n; i++, o += 12)
                nrm[i] = new Vector3(BitConverter.ToSingle(bytes, o), BitConverter.ToSingle(bytes, o + 4), BitConverter.ToSingle(bytes, o + 8));
            var mesh = new Mesh { name = name, indexFormat = n > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            mesh.SetVertices(pos);
            mesh.SetNormals(nrm);
            mesh.subMeshCount = info.submeshes.Length;
            for (int s = 0; s < info.submeshes.Length; s++)
            {
                var idx = new int[info.submeshes[s]];
                for (int i = 0; i < idx.Length; i++, o += 4) idx[i] = (int)BitConverter.ToUInt32(bytes, o);
                mesh.SetTriangles(idx, s, false);
            }
            mesh.RecalculateBounds();
            var mats = new Material[info.materials.Length];
            for (int i = 0; i < mats.Length; i++) mats[i] = MaterialFor(info.materials[i]);
            var e = new ModelMesh { mesh = mesh, materials = mats };
            cache[name] = e;
            return e;
        }

        static Material MaterialFor(MatInfo m)
        {
            string key = m.name + "|" + m.kind;
            if (matCache.TryGetValue(key, out var hit) && hit != null) return hit;
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            var mat = new Material(shader) { name = m.name };
            var c = new Color(m.color[0], m.color[1], m.color[2]);
            // exported colours are sRGB; URP works in linear space
            mat.SetColor("_BaseColor", c);
            mat.SetFloat("_Metallic", m.metallic);
            mat.SetFloat("_Smoothness", m.smoothness);
            if (m.emission > 0)
            {
                mat.EnableKeyword("_EMISSION");
                mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                mat.SetColor("_EmissionColor", c * m.emission);
            }
            matCache[key] = mat;
            return mat;
        }
    }
}
