using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;
using Newtonsoft.Json;

namespace Outpost.Content
{
    /// <summary>One exported battlefield (StreamingAssets/Outpost/maps/ID*.{json,png,bin}).</summary>
    public class MapData
    {
        public string id, name, theme;
        public int tile, w, h, px_w, px_h, height_step, height_w, height_h, max_slots;
        public string[] tiles;
        public SiteData[] sites;
        public float[][] relays;
        public float[][] crates;

        [NonSerialized] public float[] heights;

        public class SiteData
        {
            public int id;
            public float x, y;
            public string main;
            public bool wild;
            public float[][] slots;
            public float[][] sockets;
        }
    }

    /// <summary>
    /// Builds the terrain mesh for an exported map and converts between game pixels and world space.
    /// Game pixel (x, y-down) -> Unity (x * 0.1, height, -y * 0.1).
    /// </summary>
    public static class MapBuilder
    {
        public const float Unit = 0.1f;          // Unity units per game pixel
        public static float HeightScale = 2.2f;  // exaggerates the painted height field so cliffs read in 3D
        public static Texture2D LastAlbedo;      // the painted ground of the last terrain built (minimap)

        public static string Root => Path.Combine(Application.streamingAssetsPath, "Outpost", "maps");

        public static string[] Available()
        {
            if (!Directory.Exists(Root)) return new string[0];
            var list = new List<string>();
            foreach (var f in Directory.GetFiles(Root, "*.json")) list.Add(Path.GetFileNameWithoutExtension(f));
            list.Sort();
            return list.ToArray();
        }

        public static MapData Load(string id)
        {
            var map = JsonConvert.DeserializeObject<MapData>(File.ReadAllText(Path.Combine(Root, id + ".json")));
            var bytes = File.ReadAllBytes(Path.Combine(Root, id + "_height.bin"));
            map.heights = new float[map.height_w * map.height_h];
            Buffer.BlockCopy(bytes, 0, map.heights, 0, bytes.Length);
            FlattenBases(map);
            return map;
        }

        /// <summary>Levels the ground under every base and expansion site so platforms sit flush.</summary>
        static void FlattenBases(MapData m)
        {
            foreach (var s in m.sites)
            {
                float radius = s.main != null ? 190f : 70f;
                float target = RawHeight(m, s.x, s.y);
                int step = m.height_step;
                int cx = (int)(s.x / step), cy = (int)(s.y / step), r = (int)(radius / step) + 3;
                for (int gy = Math.Max(0, cy - r); gy <= Math.Min(m.height_h - 1, cy + r); gy++)
                for (int gx = Math.Max(0, cx - r); gx <= Math.Min(m.height_w - 1, cx + r); gx++)
                {
                    float d = Mathf.Sqrt(Sq(gx * step - s.x) + Sq((gy * step - s.y) * 1.0f));
                    float k = Mathf.Clamp01((radius + 30f - d) / 30f);
                    int i = gy * m.height_w + gx;
                    m.heights[i] = Mathf.Lerp(m.heights[i], target, k);
                }
            }
        }

        static float Sq(float v) => v * v;

        static float RawHeight(MapData m, float px, float py)
        {
            float fx = Mathf.Clamp(px / m.height_step, 0, m.height_w - 1.001f);
            float fy = Mathf.Clamp(py / m.height_step, 0, m.height_h - 1.001f);
            int x0 = (int)fx, y0 = (int)fy;
            float tx = fx - x0, ty = fy - y0;
            float a = m.heights[y0 * m.height_w + x0], b = m.heights[y0 * m.height_w + x0 + 1];
            float c = m.heights[(y0 + 1) * m.height_w + x0], d = m.heights[(y0 + 1) * m.height_w + x0 + 1];
            return Mathf.Lerp(Mathf.Lerp(a, b, tx), Mathf.Lerp(c, d, tx), ty);
        }

        /// <summary>World position on the ground under a game-pixel coordinate.</summary>
        public static Vector3 Ground(MapData m, float px, float py) =>
            new Vector3(px * Unit, RawHeight(m, px, py) * HeightScale * Unit, -py * Unit);

        public static GameObject BuildTerrain(MapData m)
        {
            int W = m.height_w, H = m.height_h;
            var verts = new Vector3[W * H];
            var uvs = new Vector2[W * H];
            for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                int i = y * W + x;
                float px = x * m.height_step, py = y * m.height_step;
                verts[i] = new Vector3(px * Unit, m.heights[i] * HeightScale * Unit, -py * Unit);
                uvs[i] = new Vector2(px / m.px_w, 1f - py / m.px_h);
            }
            var tris = new int[(W - 1) * (H - 1) * 6];
            int t = 0;
            for (int y = 0; y < H - 1; y++)
            for (int x = 0; x < W - 1; x++)
            {
                int i = y * W + x;
                // winding chosen so faces point up given z = -py
                tris[t++] = i; tris[t++] = i + 1; tris[t++] = i + W;
                tris[t++] = i + 1; tris[t++] = i + W + 1; tris[t++] = i + W;
            }
            var mesh = new Mesh { name = m.id + "_terrain", indexFormat = IndexFormat.UInt32 };
            mesh.vertices = verts;
            mesh.uv = uvs;
            mesh.triangles = tris;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            var tex = new Texture2D(2, 2, TextureFormat.RGB24, true);
            tex.LoadImage(File.ReadAllBytes(Path.Combine(Root, m.id + "_albedo.png")));
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.anisoLevel = 8;
            tex.filterMode = FilterMode.Trilinear;
            LastAlbedo = tex;

            var mat = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = m.id + "_ground" };
            mat.SetTexture("_BaseMap", tex);
            mat.SetColor("_BaseColor", Color.white);
            mat.SetFloat("_Smoothness", 0.08f);
            mat.SetFloat("_Metallic", 0f);

            var go = new GameObject("Terrain");
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = ShadowCastingMode.On;
            mr.receiveShadows = true;
            go.AddComponent<MeshCollider>().sharedMesh = mesh;
            return go;
        }
    }
}
