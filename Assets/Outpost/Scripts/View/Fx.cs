using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Outpost.Content;
using Outpost.Sim;

namespace Outpost.View
{
    /// <summary>
    /// The battle's view layer: creates / removes entity views and draws shots (tracers, projectiles,
    /// lightning, beams, impact flashes). Simple pooled primitives for now; real VFX come in phase 4.
    /// </summary>
    public class Fx : MonoBehaviour, IBattleFx
    {
        public MapData map;
        public Transform unitsRoot;
        public readonly List<(float t, string faction, string text)> messages = new List<(float, string, string)>();

        static readonly Dictionary<Color, Material> unlit = new Dictionary<Color, Material>();
        static Mesh ringMesh;

        class Live { public Transform t; public LineRenderer line; public float life, total; public Vector3 scale; public bool pooled, hold, grow; }
        readonly List<Live> live = new List<Live>();
        readonly Stack<LineRenderer> linePool = new Stack<LineRenderer>();
        readonly Stack<Transform> spherePool = new Stack<Transform>();
        readonly Dictionary<Projectile, Transform> shots = new Dictionary<Projectile, Transform>();

        static readonly Color PlayerTracer = new Color(1f, 0.85f, 0.45f);
        static readonly Color AlienTracer = new Color(0.55f, 1f, 0.6f);
        static readonly Color LightningColor = new Color(0.6f, 0.85f, 1f);

        public static Material Unlit(Color c)
        {
            if (unlit.TryGetValue(c, out var m) && m != null) return m;
            m = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { name = "fx_" + ColorUtility.ToHtmlStringRGB(c) };
            m.SetColor("_BaseColor", c);
            m.SetFloat("_Cull", 0f);          // double-sided (rings, flat decals)
            unlit[c] = m;
            return m;
        }

        /// <summary>A flat selection ring lying on the ground (radius in world units).</summary>
        public static Transform MakeRing(Transform parent, float radius, Color c)
        {
            if (ringMesh == null)
            {
                const int seg = 48;
                var v = new Vector3[seg * 2];
                var tri = new int[seg * 6];
                for (int i = 0; i < seg; i++)
                {
                    float a = i / (float)seg * Mathf.PI * 2f;
                    var d = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                    v[i * 2] = d * 0.86f;
                    v[i * 2 + 1] = d;
                    int j = (i + 1) % seg;
                    tri[i * 6] = i * 2; tri[i * 6 + 1] = j * 2; tri[i * 6 + 2] = i * 2 + 1;
                    tri[i * 6 + 3] = i * 2 + 1; tri[i * 6 + 4] = j * 2; tri[i * 6 + 5] = j * 2 + 1;
                }
                ringMesh = new Mesh { name = "ring", vertices = v, triangles = tri };
                ringMesh.RecalculateNormals();
            }
            var go = new GameObject("ring");
            go.transform.SetParent(parent, false);
            go.transform.localScale = Vector3.one * radius;
            go.AddComponent<MeshFilter>().sharedMesh = ringMesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = Unlit(c);
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            return go.transform;
        }

        Vector3 W(Vector2 px, float lift = 0.9f) => MapBuilder.Ground(map, px.x, px.y) + Vector3.up * lift;

        Vector3 Muzzle(Entity e)
        {
            var p = MapBuilder.Ground(map, e.x, e.y) + Vector3.up * 0.8f;
            if (e.view is EntityView v && v != null) p.y = v.transform.position.y + Mathf.Min(1.2f, v.topHeight * 0.55f);
            return p;
        }

        Vector3 Body(Vector2 px, bool air) => W(px, air ? 5.8f : 0.8f);

        // ------------------------------------------------------------------ IBattleFx
        public void Spawned(Entity e) => EntityView.Create(e, map, unitsRoot);

        public void Died(Entity e)
        {
            if (e.view is EntityView v && v != null)
            {
                var c = e.faction == "player" ? new Color(1f, 0.6f, 0.25f) : new Color(0.7f, 1f, 0.4f);
                float size = e.IsUnit ? 1.4f + e.radius * 0.04f : 4f;
                Flash(v.transform.position + Vector3.up * 0.6f, c, size, e.IsUnit ? 0.35f : 0.8f);
                if (!e.IsUnit) Flash(v.transform.position + Vector3.up * 1.5f, new Color(1f, 0.9f, 0.6f), size * 0.6f, 0.5f);
                v.Die();
            }
        }

        public void Shot(Entity src, Vector2 from, Vector2 to, string faction, int count)
        {
            var a = Muzzle(src);
            var b = Body(to, src is Unit u && u.target != null && u.target.flying);
            for (int i = 0; i < count; i++)
            {
                var j = Random.insideUnitSphere * 0.5f;
                Line(new[] { a + j * 0.6f, b + j }, faction == "player" ? PlayerTracer : AlienTracer, 0.07f, 0.07f);
            }
        }

        public void Impact(Vector2 at, string faction, float size, bool air)
        {
            var c = faction == "player" ? new Color(1f, 0.75f, 0.35f) : new Color(0.6f, 1f, 0.45f);
            Flash(Body(at, air), c, Mathf.Clamp(size * 0.06f, 0.4f, 6f), size > 20 ? 0.35f : 0.12f);
        }

        public void ProjectileSpawned(Projectile p)
        {
            var c = p.kind == "acid" ? new Color(0.55f, 1f, 0.3f)
                : p.kind == "rocket" ? new Color(1f, 0.55f, 0.2f)
                : p.kind == "lob" ? new Color(1f, 0.8f, 0.4f)
                : p.faction == "player" ? new Color(0.6f, 0.9f, 1f) : new Color(0.7f, 1f, 0.5f);
            var t = Sphere(c);
            t.localScale = Vector3.one * (p.kind == "lob" || p.kind == "shell" ? 0.55f : 0.35f);
            t.position = Muzzle(p.src);
            shots[p] = t;
        }

        public void ProjectileEnded(Projectile p)
        {
            if (shots.TryGetValue(p, out var t))
            {
                Release(t);
                shots.Remove(p);
            }
        }

        public void Lightning(List<Vector2> points, bool air)
        {
            var pts = new List<Vector3>();
            for (int i = 0; i < points.Count; i++)
            {
                var a = i == 0 ? W(points[0], 2.2f) : Body(points[i], air);
                if (i > 0)
                {
                    var prev = pts[pts.Count - 1];
                    for (int k = 1; k < 4; k++)
                        pts.Add(Vector3.Lerp(prev, a, k / 4f) + Random.insideUnitSphere * 0.5f);
                }
                pts.Add(a);
            }
            Line(pts.ToArray(), LightningColor, 0.12f, 0.16f);
        }

        public void Beam(Vector2 from, Vector2 to)
        {
            Line(new[] { W(from, 1.6f), W(to, 1f) }, new Color(0.7f, 0.95f, 1f), 0.25f, 0.22f);
        }

        public void Message(string faction, string text) => messages.Add((Time.time, faction, text));

        static Color KindColor(string kind)
        {
            switch (kind)
            {
                case "fire":
                case "blast": return new Color(1f, 0.55f, 0.15f);
                case "stim": return new Color(1f, 0.3f, 0.3f);
                case "goo":
                case "acid": return new Color(0.55f, 1f, 0.35f);
                case "burrow": return new Color(0.6f, 0.42f, 0.26f);
                case "upgrade": return new Color(1f, 0.9f, 0.45f);
                default: return new Color(0.45f, 0.9f, 1f);
            }
        }

        /// <summary>A ring that spreads out to the radius (game pixels) and fades: abilities, stomps, upgrades.</summary>
        public void Ring(Vector2 at, float radius, string kind)
        {
            var r = MakeRing(transform, Mathf.Max(0.6f, radius * MapBuilder.Unit), KindColor(kind));
            r.position = W(at, 0.18f);
            live.Add(new Live { t = r, life = 0.5f, total = 0.5f, scale = r.localScale, grow = true });
        }

        /// <summary>Marks a burning / acid patch for as long as it lasts.</summary>
        public void Zone(Vector2 at, float radius, float life, string kind)
        {
            var c = KindColor(kind);
            foreach (float k in new[] { 1f, 0.62f, 0.28f })
            {
                var r = MakeRing(transform, radius * MapBuilder.Unit * k, c);
                r.position = W(at, 0.16f);
                live.Add(new Live { t = r, life = life, total = life, scale = r.localScale, hold = true });
            }
            Flash(W(at, 0.8f), c, Mathf.Clamp(radius * 0.05f, 1f, 6f), 0.4f);
        }

        public void Trail(Vector2 from, Vector2 to, string faction)
        {
            Line(new[] { W(from, 1.4f), W(to, 0.6f) }, faction == "player" ? new Color(1f, 0.6f, 0.25f) : AlienTracer, 0.14f, 0.14f);
        }

        // ------------------------------------------------------------------ helpers
        public void Flash(Vector3 at, Color c, float size, float life)
        {
            var t = Sphere(c);
            t.position = at;
            t.localScale = Vector3.one * size;
            live.Add(new Live { t = t, life = life, total = life, scale = Vector3.one * size, pooled = true });
        }

        /// <summary>A ring that shrinks away (move orders, rally points).</summary>
        public void Marker(Vector3 at, Color c, float radius)
        {
            var r = MakeRing(transform, radius, c);
            r.position = at + Vector3.up * 0.15f;
            live.Add(new Live { t = r, life = 0.45f, total = 0.45f, scale = r.localScale });
        }

        void Line(Vector3[] pts, Color c, float width, float life)
        {
            var lr = linePool.Count > 0 ? linePool.Pop() : NewLine();
            lr.gameObject.SetActive(true);
            lr.sharedMaterial = Unlit(c);
            lr.positionCount = pts.Length;
            lr.SetPositions(pts);
            lr.widthMultiplier = width;
            live.Add(new Live { line = lr, life = life, total = life });
        }

        LineRenderer NewLine()
        {
            var go = new GameObject("line");
            go.transform.SetParent(transform, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.shadowCastingMode = ShadowCastingMode.Off;
            lr.receiveShadows = false;
            lr.numCapVertices = 2;
            return lr;
        }

        Transform Sphere(Color c)
        {
            Transform t;
            if (spherePool.Count > 0) t = spherePool.Pop();
            else
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                DestroyImmediate(go.GetComponent<Collider>());
                go.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
                go.transform.SetParent(transform, false);
                t = go.transform;
            }
            t.gameObject.SetActive(true);
            t.GetComponent<MeshRenderer>().sharedMaterial = Unlit(c);
            return t;
        }

        void Release(Transform t)
        {
            t.gameObject.SetActive(false);
            spherePool.Push(t);
        }

        void LateUpdate()
        {
            float dt = Time.deltaTime;
            for (int i = live.Count - 1; i >= 0; i--)
            {
                var l = live[i];
                l.life -= dt;
                float k = Mathf.Clamp01(l.life / l.total);
                if (l.line != null) l.line.widthMultiplier = Mathf.Max(0.01f, l.line.widthMultiplier * (0.6f + 0.4f * k));
                else if (l.t != null)
                    l.t.localScale = l.scale * (l.hold ? 1f : l.grow ? Mathf.Lerp(0.25f, 1f, 1f - k * k)
                        : l.pooled ? Mathf.Lerp(0.2f, 1f, Mathf.Sqrt(k)) : k);
                if (l.life > 0) continue;
                if (l.line != null) { l.line.gameObject.SetActive(false); linePool.Push(l.line); }
                else if (l.pooled) Release(l.t);
                else if (l.t != null) Destroy(l.t.gameObject);
                live.RemoveAt(i);
            }
            foreach (var kv in shots)
            {
                var p = kv.Key;
                var t = kv.Value;
                if (p.lob)
                {
                    float f = Mathf.Clamp01(p.t / p.total);
                    var a = Muzzle(p.src);
                    var b = W(p.dest, 0.5f);
                    t.position = Vector3.Lerp(a, b, f) + Vector3.up * Mathf.Sin(f * Mathf.PI) * 9f;
                }
                else t.position = Body(p.pos, p.target != null && p.target.flying && (p.dest - p.pos).magnitude < 120)
                                  + Vector3.up * 0.3f;
            }
        }
    }
}
