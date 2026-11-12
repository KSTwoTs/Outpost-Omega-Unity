using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;
using Newtonsoft.Json;

namespace Outpost.Content
{
    /// <summary>
    /// Loads the meshes exported by tools/export_unity.py (StreamingAssets/Outpost/models/NAME.json + .bin)
    /// and builds URP Lit materials from their colour / kind. Meshes and materials are cached.
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
        }

        class Entry
        {
            public Mesh mesh;
            public Material[] materials;
        }

        static readonly Dictionary<string, Entry> cache = new Dictionary<string, Entry>();
        static readonly Dictionary<string, Material> matCache = new Dictionary<string, Material>();

        public static string Root => Path.Combine(Application.streamingAssetsPath, "Outpost");
        static string PakIndex => Path.Combine(Root, "models.pak.json");
        static string PakData => Path.Combine(Root, "models.pak.bin");

        static Dictionary<string, ModelInfo> index;

        static Dictionary<string, ModelInfo> Index
        {
            get
            {
                if (index == null)
                    index = File.Exists(PakIndex)
                        ? JsonConvert.DeserializeObject<Dictionary<string, ModelInfo>>(File.ReadAllText(PakIndex))
                        : new Dictionary<string, ModelInfo>();
                return index;
            }
        }

        public static bool Has(string name) => Index.ContainsKey(name);

        /// <summary>Forget everything loaded (after re-exporting models).</summary>
        public static void Reload()
        {
            index = null;
            cache.Clear();
        }

        /// <summary>A new GameObject showing the model, or null if the model wasn't exported.</summary>
        public static GameObject Spawn(string name, Transform parent = null)
        {
            var e = Load(name);
            if (e == null) return null;
            var go = new GameObject(name);
            if (parent != null) go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = e.mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterials = e.materials;
            mr.shadowCastingMode = ShadowCastingMode.On;
            return go;
        }

        static Entry Load(string name)
        {
            if (cache.TryGetValue(name, out var hit)) return hit;
            if (!Index.TryGetValue(name, out var info))
            {
                Debug.LogWarning($"[Outpost] model '{name}' not exported");
                cache[name] = null;
                return null;
            }
            var bytes = new byte[info.length];
            using (var fs = File.OpenRead(PakData))
            {
                fs.Seek(info.offset, SeekOrigin.Begin);
                int read = 0;
                while (read < bytes.Length) read += fs.Read(bytes, read, bytes.Length - read);
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
            var e = new Entry { mesh = mesh, materials = mats };
            cache[name] = e;
            return e;
        }

        static Material MaterialFor(MatInfo m)
        {
            string key = m.name + "|" + m.kind;
            if (matCache.TryGetValue(key, out var hit)) return hit;
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
