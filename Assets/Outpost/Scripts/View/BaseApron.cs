using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Outpost.Content;

namespace Outpost.View
{
    /// <summary>
    /// The connected base platform drawn from the map's real slot and socket positions: a central pad,
    /// a pad under every building slot, walkways joining them to the core and to each other, and spurs
    /// out to the turret sockets. Human bases are plated concrete with cyan light strips; alien bases are
    /// creep with glowing orange veins.
    /// </summary>
    public static class BaseApron
    {
        class Layer
        {
            public readonly List<Vector3> v = new List<Vector3>();
            public readonly List<int> t = new List<int>();
        }

        public static GameObject Build(MapData map, MapData.SiteData s, bool human, Transform parent)
        {
            var root = new GameObject(human ? "Apron (human)" : "Apron (alien)");
            root.transform.SetParent(parent, false);
            float baseY = MapBuilder.Ground(map, s.x, s.y).y;
            var c = new Vector2(s.x, s.y);
            var rim = new Layer();
            var top = new Layer();
            var glow = new Layer();
            float wobble = human ? 0f : 0.16f;
            int seed = s.id * 7 + 3;

            // core pad
            Disc(rim, c, 82, wobble, seed);
            Disc(top, c, 74, wobble, seed);
            for (int i = 0; i < s.slots.Length; i++)
            {
                var p = new Vector2(s.slots[i][0], s.slots[i][1]);
                var q = new Vector2(s.slots[(i + 1) % s.slots.Length][0], s.slots[(i + 1) % s.slots.Length][1]);
                Strip(rim, c, p, 30, wobble, seed + i);
                Strip(top, c, p, 22, wobble, seed + i);
                Strip(rim, p, q, 22, wobble, seed + 40 + i);
                Strip(top, p, q, 15, wobble, seed + 40 + i);
                Disc(rim, p, 40, wobble, seed + 80 + i);
                Disc(top, p, 34, wobble, seed + 80 + i);
                // light strip / vein from the core towards the slot
                Strip(glow, Vector2.Lerp(c, p, 0.22f), Vector2.Lerp(c, p, 0.72f), human ? 2.2f : 2.6f, human ? 0f : 0.5f, seed + 120 + i);
            }
            foreach (var sk in s.sockets)
            {
                var p = new Vector2(sk[0], sk[1]);
                var from = c + (p - c).normalized * 100f;
                Strip(rim, from, p, 18, wobble, seed + 200);
                Strip(top, from, p, 11, wobble, seed + 200);
                Disc(rim, p, 24, wobble, seed + 210);
                Disc(top, p, 19, wobble, seed + 210);
                if (human) Ring(glow, p, 19, 1.6f);
            }
            if (human) Ring(glow, c, 74, 2f);

            Color rimC = human ? new Color(0.13f, 0.14f, 0.16f) : new Color(0.05f, 0.09f, 0.09f);
            Color topC = human ? new Color(0.30f, 0.31f, 0.34f) : new Color(0.09f, 0.17f, 0.17f);
            Color glowC = human ? new Color(0.35f, 0.85f, 1f) : new Color(1f, 0.55f, 0.15f);
            Emit(root.transform, "rim", rim, baseY + 0.05f, Lit(rimC, human ? 0.25f : 0.5f));
            Emit(root.transform, "top", top, baseY + 0.09f, Lit(topC, human ? 0.35f : 0.6f));
            Emit(root.transform, "glow", glow, baseY + 0.12f, Fx.Unlit(glowC));
            return root;
        }

        static Material Lit(Color c, float smooth)
        {
            var m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            m.SetColor("_BaseColor", c);
            m.SetFloat("_Smoothness", smooth);
            m.SetFloat("_Metallic", 0f);
            m.SetFloat("_Cull", 0f);
            return m;
        }

        static float Noise(int seed, float a) =>
            Mathf.Sin(a * 3f + seed * 1.7f) * 0.5f + Mathf.Sin(a * 7f + seed * 0.9f) * 0.3f + Mathf.Sin(a * 13f + seed) * 0.2f;

        static void Disc(Layer l, Vector2 c, float r, float wobble, int seed, int seg = 40)
        {
            int start = l.v.Count;
            l.v.Add(new Vector3(c.x, 0, c.y));
            for (int i = 0; i < seg; i++)
            {
                float a = i / (float)seg * Mathf.PI * 2f;
                float rr = r * (1f + wobble * Noise(seed, a));
                l.v.Add(new Vector3(c.x + Mathf.Cos(a) * rr, 0, c.y + Mathf.Sin(a) * rr));
            }
            for (int i = 0; i < seg; i++)
            {
                l.t.Add(start);
                l.t.Add(start + 1 + i);
                l.t.Add(start + 1 + (i + 1) % seg);
            }
        }

        static void Strip(Layer l, Vector2 a, Vector2 b, float width, float wobble, int seed, int seg = 12)
        {
            var d = b - a;
            if (d.sqrMagnitude < 1) return;
            var n = new Vector2(-d.y, d.x).normalized;
            int start = l.v.Count;
            for (int i = 0; i <= seg; i++)
            {
                float f = i / (float)seg;
                var p = a + d * f;
                float w = width * (1f + wobble * Noise(seed, f * 6f));
                var sway = n * (wobble > 0 ? Mathf.Sin(f * Mathf.PI * 2f + seed) * width * 0.35f : 0f);
                var l0 = p + sway + n * w * 0.5f;
                var l1 = p + sway - n * w * 0.5f;
                l.v.Add(new Vector3(l0.x, 0, l0.y));
                l.v.Add(new Vector3(l1.x, 0, l1.y));
                if (i == 0) continue;
                int k = start + i * 2;
                l.t.Add(k - 2); l.t.Add(k - 1); l.t.Add(k);
                l.t.Add(k - 1); l.t.Add(k + 1); l.t.Add(k);
            }
        }

        static void Ring(Layer l, Vector2 c, float r, float width, int seg = 48)
        {
            int start = l.v.Count;
            for (int i = 0; i <= seg; i++)
            {
                float a = i / (float)seg * Mathf.PI * 2f;
                var d = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                var o = c + d * r;
                var n = c + d * (r - width);
                l.v.Add(new Vector3(o.x, 0, o.y));
                l.v.Add(new Vector3(n.x, 0, n.y));
                if (i == 0) continue;
                int k = start + i * 2;
                l.t.Add(k - 2); l.t.Add(k - 1); l.t.Add(k);
                l.t.Add(k - 1); l.t.Add(k + 1); l.t.Add(k);
            }
        }

        /// <summary>Turns a layer (game pixels in x/z) into a flat mesh at height y, visible from both sides.</summary>
        static void Emit(Transform parent, string name, Layer l, float y, Material mat)
        {
            var verts = new Vector3[l.v.Count];
            for (int i = 0; i < verts.Length; i++)
                verts[i] = new Vector3(l.v[i].x * MapBuilder.Unit, y, -l.v[i].z * MapBuilder.Unit);
            var tris = new int[l.t.Count * 2];
            for (int i = 0; i < l.t.Count; i += 3)
            {
                tris[i] = l.t[i]; tris[i + 1] = l.t[i + 1]; tris[i + 2] = l.t[i + 2];
                int j = l.t.Count + i;
                tris[j] = l.t[i]; tris[j + 1] = l.t[i + 2]; tris[j + 2] = l.t[i + 1];
            }
            var mesh = new Mesh { name = name, indexFormat = IndexFormat.UInt32 };
            mesh.vertices = verts;
            mesh.triangles = tris;
            var nrm = new Vector3[verts.Length];
            for (int i = 0; i < nrm.Length; i++) nrm[i] = Vector3.up;
            mesh.normals = nrm;
            mesh.RecalculateBounds();
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = true;
        }
    }
}
