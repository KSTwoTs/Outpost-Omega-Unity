// EffectPool.cs · roadmap step M1.5 · combat effects (see ARCHITECTURE.md).
// Recycles every effect object. Fx, WeaponEffects and AbilityEffects create nothing themselves: they ask the pool
// for a piece (a glowing quad, a flat ground quad or a line), set how it animates, and the pool takes it back when
// its life runs out. Particles (sparks, motes, smoke, chunks) go through four shared ParticleSystems with Emit(),
// which recycle internally. The number of live pieces is capped (MaxLive): past it the oldest piece is reused.

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Outpost.View
{
    /// <summary>The soft textures effects are drawn with (made in code, see EffectPool.MakeTexture).</summary>
    public enum FxTex { Glow, Ring, Line, Smoke, Disc, Star }

    public enum FxBlend { Add, Alpha }

    /// <summary>How a quad piece faces: towards the camera, or lying flat on the ground.</summary>
    public enum FxFacing { Billboard, Flat }

    /// <summary>One pooled visual piece: a quad (billboard or flat) or a line. Fields describe its animation.</summary>
    public sealed class FxPiece
    {
        public GameObject go;
        public Transform t;
        public MeshRenderer quad;
        public LineRenderer line;
        public FxFacing facing;
        public FxTex tex;
        public FxBlend blend;
        /// <summary>Held pieces do not age; whoever holds them calls EffectPool.Release (projectiles in flight).</summary>
        public bool held;
        public float life, total;
        public float size0, size1;              // quad size (world units) from start to end of life
        public float width0, width1;            // line width
        public float spin, angle;               // degrees per second / current degrees
        public float delay;                     // seconds before it appears
        public float fadeIn;                    // fraction of life spent fading in
        public float fadeOut = 1f;              // fraction of life spent fading out (1 = fades all the way)
        public float flicker;                   // 0..1 random brightness wobble (fire)
        public float ease = 2f;                 // size easing power (1 linear, 2 fast then slow)
        public Color color, color2;             // color2: the far end of a line
        public Vector3 pos, vel;
        public float drag;
        public Vector3[] points;                // line points
        public int pointCount;
        internal bool live;

        public bool Live => live;
        public float Progress => total > 0 ? Mathf.Clamp01(1f - life / total) : 1f;
    }

    /// <summary>Recycles effect pieces and owns the shared materials, textures and particle systems.</summary>
    public class EffectPool : IDisposable
    {
        public const int MaxLive = 1600;
        public const int MaxRepeaters = 256;

        public readonly Transform root;
        /// <summary>Billboards face this point when set (edit-mode stills); otherwise the main camera.</summary>
        public Vector3? viewFrom;
        public int Created { get; private set; }
        public int LiveCount => live.Count;
        public int FreeCount => quadFree.Count + lineFree.Count;
        public int RepeaterCount => repeaters.Count;
        public int Peak { get; private set; }
        public int Recycled { get; private set; }     // live pieces taken over because MaxLive was reached
        public int Particles => (sparks ? sparks.particleCount : 0) + (motes ? motes.particleCount : 0) +
                                (smoke ? smoke.particleCount : 0) + (chunks ? chunks.particleCount : 0);

        readonly List<FxPiece> live = new List<FxPiece>();
        readonly Stack<FxPiece> quadFree = new Stack<FxPiece>();
        readonly Stack<FxPiece> lineFree = new Stack<FxPiece>();
        readonly Material[] mats = new Material[24];
        readonly Texture2D[] texs = new Texture2D[6];
        readonly MaterialPropertyBlock mpb = new MaterialPropertyBlock();
        readonly List<Repeater> repeaters = new List<Repeater>();
        readonly Vector3[] pair = new Vector3[2];
        readonly ParticleSystem[] systems;
        Mesh quadMesh;
        readonly ParticleSystem sparks, motes, smoke, chunks;
        static readonly int ColorId = Shader.PropertyToID("_Color");

        class Repeater { public float life, every, next, age; public Action<float> fire; }

        public EffectPool(Transform parent)
        {
            var go = new GameObject("Effects");
            go.transform.SetParent(parent, false);
            root = go.transform;
            sparks = MakeParticles("sparks", Mat(FxTex.Glow, FxBlend.Add), true, 1.6f, 1.5f, false);
            motes = MakeParticles("motes", Mat(FxTex.Glow, FxBlend.Add), false, 0f, 2.5f, false);
            smoke = MakeParticles("smoke", Mat(FxTex.Smoke, FxBlend.Alpha), false, -0.06f, 1.2f, true);
            chunks = MakeParticles("chunks", Mat(FxTex.Disc, FxBlend.Alpha), false, 2.2f, 0.4f, false);
            systems = new[] { sparks, motes, smoke, chunks };
        }

        // ------------------------------------------------------------------ pieces
        /// <summary>A quad piece. Billboards face the camera; flat ones lie on the ground.</summary>
        public FxPiece Quad(FxFacing facing, FxTex tex, FxBlend blend, Vector3 at, Color c, float size0, float size1, float life)
        {
            var p = Take(false);
            p.facing = facing;
            p.tex = tex;
            p.blend = blend;
            p.quad.sharedMaterial = Mat(tex, blend, facing == FxFacing.Flat);
            p.pos = at;
            p.color = p.color2 = c;
            p.size0 = size0;
            p.size1 = size1;
            p.life = p.total = Mathf.Max(0.01f, life);
            p.angle = facing == FxFacing.Flat ? UnityEngine.Random.Range(0f, 360f) : 0f;
            Apply(p, Eye());
            return p;
        }

        public FxPiece Glow(Vector3 at, Color c, float size0, float size1, float life) =>
            Quad(FxFacing.Billboard, FxTex.Glow, FxBlend.Add, at, c, size0, size1, life);

        public FxPiece Flat(FxTex tex, FxBlend blend, Vector3 at, Color c, float size0, float size1, float life) =>
            Quad(FxFacing.Flat, tex, blend, at, c, size0, size1, life);

        /// <summary>A glowing line through the points (tracers, beams, lightning). The pool keeps its own copy.</summary>
        public FxPiece Line(IList<Vector3> pts, Color c, float width0, float width1, float life, Color? end = null)
        {
            var p = Take(true);
            p.tex = FxTex.Line;
            p.blend = FxBlend.Add;
            if (p.points == null || p.points.Length < pts.Count) p.points = new Vector3[Mathf.Max(8, pts.Count)];
            for (int i = 0; i < pts.Count; i++) p.points[i] = pts[i];
            p.pointCount = pts.Count;
            p.line.positionCount = pts.Count;
            for (int i = 0; i < pts.Count; i++) p.line.SetPosition(i, pts[i]);
            p.color = c;
            p.color2 = end ?? c;
            p.width0 = width0;
            p.width1 = width1;
            p.life = p.total = Mathf.Max(0.01f, life);
            Apply(p, Vector3.zero);
            return p;
        }

        public FxPiece Line(Vector3 a, Vector3 b, Color c, float width0, float width1, float life, Color? end = null)
        {
            pair[0] = a;
            pair[1] = b;
            return Line(pair, c, width0, width1, life, end);
        }

        /// <summary>Moves a two-point line (a beam following its target).</summary>
        public void SetLine(FxPiece p, Vector3 a, Vector3 b)
        {
            if (p == null || !p.live || p.line == null) return;
            p.points[0] = a;
            p.points[1] = b;
            p.line.SetPosition(0, a);
            p.line.SetPosition(1, b);
        }

        /// <summary>Hands a piece back early (a projectile that landed). Safe to call twice.</summary>
        public void Release(FxPiece p)
        {
            if (p == null || !p.live) return;
            int i = live.IndexOf(p);
            if (i >= 0)
            {
                live[i] = live[live.Count - 1];
                live.RemoveAt(live.Count - 1);
            }
            Return(p);
        }

        /// <summary>Calls fire(age) every few seconds for a while (burning patches, scans, auras). Capped at MaxRepeaters.</summary>
        public void Repeat(float life, float every, Action<float> fire)
        {
            if (repeaters.Count >= MaxRepeaters || fire == null) return;
            repeaters.Add(new Repeater { life = life, every = Mathf.Max(0.02f, every), next = 0f, fire = fire });
        }

        FxPiece Take(bool isLine)
        {
            var free = isLine ? lineFree : quadFree;
            FxPiece p = null;
            if (free.Count > 0) p = free.Pop();
            else if (live.Count >= MaxLive) p = Steal(isLine);
            if (p == null) p = New(isLine);
            Reset(p);
            p.live = true;
            p.go.SetActive(true);
            live.Add(p);
            if (live.Count > Peak) Peak = live.Count;
            return p;
        }

        /// <summary>At the cap: an old non-held piece of the same kind stops early and is reused.</summary>
        FxPiece Steal(bool isLine)
        {
            for (int i = 0; i < live.Count; i++)
            {
                var o = live[i];
                if (o.held || (o.line != null) != isLine) continue;
                live.RemoveAt(i);
                o.live = false;
                Recycled++;
                return o;
            }
            return null;
        }

        static void Reset(FxPiece p)
        {
            p.held = false;
            p.spin = p.angle = p.delay = p.fadeIn = p.flicker = p.drag = 0f;
            p.fadeOut = 1f;
            p.ease = 2f;
            p.vel = Vector3.zero;
            p.width0 = p.width1 = 0.1f;
        }

        FxPiece New(bool isLine)
        {
            Created++;
            var p = new FxPiece();
            if (isLine)
            {
                p.go = new GameObject("fx_line");
                p.line = p.go.AddComponent<LineRenderer>();
                p.line.useWorldSpace = true;
                p.line.shadowCastingMode = ShadowCastingMode.Off;
                p.line.receiveShadows = false;
                p.line.numCapVertices = 3;
                p.line.textureMode = LineTextureMode.Stretch;
                p.line.alignment = LineAlignment.View;
                p.line.sharedMaterial = Mat(FxTex.Line, FxBlend.Add);
            }
            else
            {
                p.go = new GameObject("fx_quad");
                p.go.AddComponent<MeshFilter>().sharedMesh = QuadMesh();
                p.quad = p.go.AddComponent<MeshRenderer>();
                p.quad.shadowCastingMode = ShadowCastingMode.Off;
                p.quad.receiveShadows = false;
                p.quad.lightProbeUsage = LightProbeUsage.Off;
                p.quad.reflectionProbeUsage = ReflectionProbeUsage.Off;
            }
            p.go.hideFlags = HideFlags.DontSave;
            p.t = p.go.transform;
            p.t.SetParent(root, false);
            return p;
        }

        void Return(FxPiece p)
        {
            p.live = false;
            if (p.go == null) return;
            p.go.SetActive(false);
            (p.line != null ? lineFree : quadFree).Push(p);
        }

        // ------------------------------------------------------------------ particles
        /// <summary>Hot sparks flying out and falling (stretched along their motion).</summary>
        public void Sparks(Vector3 at, Color c, int n, float speed, float life, float size, Vector3 dir = default(Vector3), float spread = 1f) =>
            Emit(sparks, at, c, n, speed, life, size, dir, spread, 0f);

        /// <summary>Glowing motes that drift and slow down (energy, embers, magic dust).</summary>
        public void Motes(Vector3 at, Color c, int n, float speed, float life, float size, Vector3 dir = default(Vector3), float spread = 1f, float jitter = 0f) =>
            Emit(motes, at, c, n, speed, life, size, dir, spread, jitter);

        /// <summary>Soft smoke or dust puffs that rise slowly and grow.</summary>
        public void Smoke(Vector3 at, Color c, int n, float speed, float life, float size, float jitter = 0.3f) =>
            Emit(smoke, at, c, n, speed, life, size, Vector3.up, 0.8f, jitter);

        /// <summary>Solid bits thrown up that fall back (dirt, debris, goo).</summary>
        public void Chunks(Vector3 at, Color c, int n, float speed, float life, float size) =>
            Emit(chunks, at, c, n, speed, life, size, Vector3.up, 0.75f, 0.15f);

        void Emit(ParticleSystem ps, Vector3 at, Color c, int n, float speed, float life, float size, Vector3 dir, float spread, float jitter)
        {
            if (ps == null || n <= 0) return;
            var ep = new ParticleSystem.EmitParams();
            for (int i = 0; i < n; i++)
            {
                Vector3 d = UnityEngine.Random.onUnitSphere;
                if (dir != Vector3.zero) d = Vector3.Slerp(dir.normalized, d, spread * 0.5f).normalized;
                ep.position = at + (jitter > 0 ? UnityEngine.Random.insideUnitSphere * jitter : Vector3.zero);
                ep.velocity = d * speed * UnityEngine.Random.Range(0.45f, 1f);
                ep.startLifetime = life * UnityEngine.Random.Range(0.6f, 1f);
                ep.startSize = size * UnityEngine.Random.Range(0.7f, 1.2f);
                ep.startColor = c;
                ep.rotation = UnityEngine.Random.Range(0f, 360f);
                ps.Emit(ep, 1);
            }
        }

        ParticleSystem MakeParticles(string name, Material m, bool stretched, float gravity, float drag, bool grow)
        {
            var go = new GameObject("fx_" + name);
            go.hideFlags = HideFlags.DontSave;
            go.transform.SetParent(root, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = false;
            main.playOnAwake = false;
            main.duration = 1f;
            main.maxParticles = 3000;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = gravity;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            var em = ps.emission;
            em.enabled = false;
            var shape = ps.shape;
            shape.enabled = false;
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                grow ? new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.12f), new GradientAlphaKey(0f, 1f) }
                     : new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.8f, 0.5f), new GradientAlphaKey(0f, 1f) });
            col.color = new ParticleSystem.MinMaxGradient(g);
            var sz = ps.sizeOverLifetime;
            sz.enabled = true;
            sz.size = new ParticleSystem.MinMaxCurve(1f, grow ? AnimationCurve.Linear(0f, 0.6f, 1f, 1.8f) : AnimationCurve.Linear(0f, 1f, 1f, 0.25f));
            if (drag > 0)
            {
                var lv = ps.limitVelocityOverLifetime;
                lv.enabled = true;
                lv.limit = 1000f;
                lv.drag = drag;
                lv.multiplyDragByParticleSize = false;
                lv.multiplyDragByParticleVelocity = false;
            }
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = m;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.sortMode = ParticleSystemSortMode.None;
            if (stretched)
            {
                r.renderMode = ParticleSystemRenderMode.Stretch;
                r.velocityScale = 0.035f;
                r.lengthScale = 1.2f;
            }
            else r.renderMode = ParticleSystemRenderMode.Billboard;
            ps.Play();
            return ps;
        }

        // ------------------------------------------------------------------ update
        Vector3 Eye()
        {
            if (viewFrom.HasValue) return viewFrom.Value;
            var cam = Camera.main;
            return cam != null ? cam.transform.position : new Vector3(0, 60, -60);
        }

        /// <summary>Ages every piece by dt and takes back the finished ones. Edit mode also steps the particles.</summary>
        public void Tick(float dt)
        {
            for (int i = repeaters.Count - 1; i >= 0; i--)
            {
                var r = repeaters[i];
                r.next -= dt;
                while (r.next <= 0f && r.age < r.life)
                {
                    r.fire(r.age);
                    r.next += r.every;
                    r.age += r.every;
                }
                if (r.age >= r.life) repeaters.RemoveAt(i);
            }
            var eye = Eye();
            for (int i = live.Count - 1; i >= 0; i--)
            {
                var p = live[i];
                if (p.go == null) { live.RemoveAt(i); continue; }
                if (p.delay > 0f) p.delay -= dt;
                else if (!p.held) p.life -= dt;
                if (!p.held && p.life <= 0f)
                {
                    live[i] = live[live.Count - 1];
                    live.RemoveAt(live.Count - 1);
                    Return(p);
                    continue;
                }
                if (p.delay <= 0f && p.vel != Vector3.zero)
                {
                    p.pos += p.vel * dt;
                    if (p.drag > 0f) p.vel *= Mathf.Max(0f, 1f - p.drag * dt);
                }
                p.angle += p.spin * dt;
                Apply(p, eye);
            }
            if (!Application.isPlaying)
                foreach (var ps in systems)
                    if (ps != null) ps.Simulate(dt, false, false, false);
        }

        void Apply(FxPiece p, Vector3 eye)
        {
            float k = p.held ? 0f : p.Progress;
            float a = 1f;
            if (p.delay > 0f) a = 0f;
            else
            {
                if (p.fadeIn > 0f && k < p.fadeIn) a *= k / p.fadeIn;
                if (p.fadeOut > 0f && k > 1f - p.fadeOut) a *= (1f - k) / p.fadeOut;
                if (p.flicker > 0f) a *= 1f - p.flicker * UnityEngine.Random.value;
            }
            float e = 1f - Mathf.Pow(1f - k, p.ease);
            if (p.line != null)
            {
                p.line.widthMultiplier = Mathf.Max(0.005f, Mathf.Lerp(p.width0, p.width1, e));
                var c1 = p.color; c1.a *= a;
                var c2 = p.color2; c2.a *= a;
                p.line.startColor = c1;
                p.line.endColor = c2;
                return;
            }
            float s = Mathf.Max(0.001f, Mathf.Lerp(p.size0, p.size1, e));
            p.t.position = p.pos;
            p.t.localScale = new Vector3(s, s, s);
            if (p.facing == FxFacing.Flat) p.t.rotation = Quaternion.Euler(90f, p.angle, 0f);
            else
            {
                var d = p.pos - eye;
                p.t.rotation = d.sqrMagnitude > 1e-6f ? Quaternion.LookRotation(d) * Quaternion.Euler(0, 0, p.angle) : Quaternion.identity;
                // pulled towards the camera by half its size, so glows near the ground are not cut off by it
                if (d.sqrMagnitude > s * s) p.t.position = p.pos - d.normalized * (s * 0.5f);
            }
            var c = p.color;
            c.a *= a;
            mpb.SetColor(ColorId, c);
            p.quad.SetPropertyBlock(mpb);
        }

        /// <summary>Puts every live piece back and clears particles and repeaters (between matches, tests).</summary>
        public void Clear()
        {
            for (int i = live.Count - 1; i >= 0; i--) Return(live[i]);
            live.Clear();
            repeaters.Clear();
            foreach (var ps in systems)
                if (ps != null) ps.Clear(true);
        }

        /// <summary>Destroys everything the pool made (objects, materials, textures, mesh).</summary>
        public void Dispose()
        {
            Clear();
            foreach (var m in mats) if (m != null) UnityEngine.Object.DestroyImmediate(m);
            foreach (var t in texs) if (t != null) UnityEngine.Object.DestroyImmediate(t);
            if (quadMesh != null) UnityEngine.Object.DestroyImmediate(quadMesh);
            if (root != null) UnityEngine.Object.DestroyImmediate(root.gameObject);
            quadFree.Clear();
            lineFree.Clear();
        }

        // ------------------------------------------------------------------ materials, textures, mesh
        /// <summary>The shared material for a texture and blend (one each, owned by this pool).</summary>
        public Material Mat(FxTex tex, FxBlend blend) => Mat(tex, blend, false);

        /// <summary>Flat additive pieces (ground rings, glows on the ground) draw over the terrain whatever its bumps
        /// (ZTest Always); billboards, lines and alpha decals are depth-tested as usual.</summary>
        public Material Mat(FxTex tex, FxBlend blend, bool flat)
        {
            bool always = flat && blend == FxBlend.Add;
            int i = ((int)tex * 2 + (int)blend) * 2 + (always ? 1 : 0);
            if (mats[i] != null) return mats[i];
            var baseMat = Resources.Load<Material>(blend == FxBlend.Add ? "Effects/fx_additive" : "Effects/fx_alpha");
            Material m;
            if (baseMat != null) m = new Material(baseMat);
            else
            {
                var sh = Shader.Find(blend == FxBlend.Add ? "Outpost/FxAdditive" : "Outpost/FxAlpha");
                if (sh == null) sh = Shader.Find("Universal Render Pipeline/Unlit");
                m = new Material(sh);
            }
            m.name = "fx_" + tex + "_" + blend + (always ? "_flat" : "");
            m.mainTexture = Tex(tex);
            m.SetColor(ColorId, Color.white);
            m.SetFloat("_ZTest", always ? (float)CompareFunction.Always : (float)CompareFunction.LessEqual);
            if (always) m.renderQueue = 3020;
            m.hideFlags = HideFlags.DontSave;
            return mats[i] = m;
        }

        Texture2D Tex(FxTex kind)
        {
            int i = (int)kind;
            if (texs[i] == null) texs[i] = MakeTexture(kind);
            return texs[i];
        }

        /// <summary>Soft white textures drawn in code: alpha carries the shape, effects colour them.</summary>
        public static Texture2D MakeTexture(FxTex kind)
        {
            const int n = 64;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, true) { name = "fx_" + kind, wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave };
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float u = (x + 0.5f) / n * 2f - 1f, v = (y + 0.5f) / n * 2f - 1f;
                    float r = Mathf.Sqrt(u * u + v * v);
                    float a;
                    switch (kind)
                    {
                        case FxTex.Glow:    // hot core with a wide soft halo
                            a = Mathf.Clamp01(Mathf.Exp(-r * r * 3.5f) * 0.65f + Mathf.Exp(-r * r * 18f) * 0.55f) * Mathf.Clamp01((1f - r) * 3f);
                            break;
                        case FxTex.Ring:    // soft band near the edge
                        {
                            float d = (r - 0.86f) / 0.1f;
                            a = Mathf.Exp(-d * d) * Mathf.Clamp01((1f - r) * 12f);
                            break;
                        }
                        case FxTex.Line:    // across the line: bright centre, soft sides
                        {
                            float d = Mathf.Abs(v);
                            a = Mathf.Clamp01(Mathf.Exp(-d * d * 3f) * 0.6f + Mathf.Exp(-d * d * 25f) * 0.6f);
                            break;
                        }
                        case FxTex.Smoke:   // lumpy puff
                        {
                            float nz = Mathf.PerlinNoise(x * 0.11f + 3.7f, y * 0.11f + 1.3f) * 0.6f + Mathf.PerlinNoise(x * 0.27f, y * 0.27f + 9f) * 0.4f;
                            a = Mathf.Clamp01((1f - r) * 1.6f) * Mathf.Clamp01(0.35f + nz * 0.9f);
                            a *= a;
                            break;
                        }
                        case FxTex.Disc:    // solid round blob with a soft rim (decals, chunks)
                            a = Mathf.Clamp01((1f - r) * 5f);
                            break;
                        default:            // Star: four-point glint
                            a = Mathf.Clamp01(Mathf.Exp(-u * u * 160f) * Mathf.Exp(-v * v * 3f) + Mathf.Exp(-v * v * 160f) * Mathf.Exp(-u * u * 3f) + Mathf.Exp(-r * r * 30f)) * Mathf.Clamp01((1f - r) * 3f);
                            break;
                    }
                    px[y * n + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(a) * 255f));
                }
            tex.SetPixels32(px);
            tex.Apply(true, false);
            return tex;
        }

        Mesh QuadMesh()
        {
            if (quadMesh != null) return quadMesh;
            quadMesh = new Mesh
            {
                name = "fx_quad",
                vertices = new[] { new Vector3(-0.5f, -0.5f, 0), new Vector3(0.5f, -0.5f, 0), new Vector3(0.5f, 0.5f, 0), new Vector3(-0.5f, 0.5f, 0) },
                uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) },
                colors32 = new[] { new Color32(255, 255, 255, 255), new Color32(255, 255, 255, 255), new Color32(255, 255, 255, 255), new Color32(255, 255, 255, 255) },
                triangles = new[] { 0, 2, 1, 0, 3, 2 },
                hideFlags = HideFlags.DontSave,
            };
            quadMesh.RecalculateNormals();
            quadMesh.bounds = new Bounds(Vector3.zero, Vector3.one * 2f);
            return quadMesh;
        }
    }
}
