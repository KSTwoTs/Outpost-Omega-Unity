// WeaponEffects.cs · roadmap step M1.5 · combat effects (see ARCHITECTURE.md).
// Muzzle flashes, tracers, projectiles in flight, impacts and explosions, beams, lightning, burning / acid patches,
// deaths, and the per-tower looks for all 34 tower behaviours. Colours follow the Python game (settings.py,
// game/rts/sim.py and render.py). Everything is drawn with pieces and particles from EffectPool.

using System.Collections.Generic;
using UnityEngine;
using Outpost.Content;
using Outpost.Sim;

namespace Outpost.View
{
    /// <summary>The Python game's effect colours (settings.py) plus a few effect-only ones.</summary>
    public static class FxColors
    {
        static Color C(int r, int g, int b) => new Color(r / 255f, g / 255f, b / 255f);
        public static readonly Color Cyan = C(0, 229, 255), Energy = C(255, 214, 60), Cryo = C(120, 200, 255), Acid = C(120, 220, 60),
            Orange = C(255, 140, 40), AlienGreen = C(110, 230, 60), Magenta = C(220, 50, 200), Red = C(235, 60, 60),
            Green = C(80, 220, 110), White = C(240, 244, 255), Steel = C(196, 206, 220), Violet = C(150, 90, 255);
        public static readonly Color PlayerTracer = new Color(1f, 0.85f, 0.45f), AlienTracer = new Color(0.55f, 1f, 0.6f);
        public static readonly Color Smoke = new Color(0.25f, 0.24f, 0.23f, 0.75f), Dust = new Color(0.58f, 0.5f, 0.38f, 0.65f),
            Dirt = C(150, 104, 64), Goo = new Color(0.36f, 0.7f, 0.16f, 0.95f), Scorch = new Color(0.04f, 0.035f, 0.03f, 0.6f),
            Debris = new Color(0.2f, 0.21f, 0.23f, 1f), Tar = new Color(0.08f, 0.06f, 0.05f, 0.7f);

        /// <summary>Brighter than 1 (additive glow reads hotter; ready for bloom later).</summary>
        public static Color Hot(Color c, float k) => new Color(c.r * k, c.g * k, c.b * k, c.a);
        public static Color A(Color c, float a) { c.a = a; return c; }
        public static Color Faction(string f) => f == "player" ? Orange : AlienGreen;
    }

    /// <summary>Draws shots, projectiles, explosions, beams, lightning, patches, deaths and tower looks.</summary>
    public class WeaponEffects
    {
        readonly EffectPool pool;
        readonly List<Vector3> pts = new List<Vector3>(64);
        readonly Dictionary<Tower, float> pulseNext = new Dictionary<Tower, float>();
        float clock;

        public WeaponEffects(EffectPool pool) { this.pool = pool; }

        public void Tick(float dt)
        {
            clock += dt;
            if (pulseNext.Count > 256) pulseNext.Clear();       // towers come and go; keep the table small
        }

        static Vector3 Up(float y) => new Vector3(0, y, 0);
        static Vector3 RandFlat(float r) { var c = Random.insideUnitCircle * r; return new Vector3(c.x, 0, c.y); }

        // ------------------------------------------------------------------ basic pieces
        public void MuzzleFlash(Vector3 at, Vector3 dir, Color c, float size)
        {
            pool.Glow(at, FxColors.Hot(c, 1.6f), size, size * 1.35f, 0.08f);
            var star = pool.Quad(FxFacing.Billboard, FxTex.Star, FxBlend.Add, at, FxColors.Hot(FxColors.White, 1.2f), size * 1.5f, size * 0.7f, 0.06f);
            star.angle = Random.Range(0f, 90f);
            pool.Sparks(at, c, 2, 9f, 0.15f, 0.08f, dir, 0.7f);
        }

        /// <summary>A bullet streak with a white-hot head (Python tracers).</summary>
        public void Tracer(Vector3 a, Vector3 b, Color c, float width, float life)
        {
            pool.Line(a, b, FxColors.A(c, 0.35f), width, width * 0.5f, life, FxColors.A(c, 1f));
            pool.Line(Vector3.Lerp(a, b, 0.6f), b, FxColors.A(FxColors.Hot(FxColors.White, 1.2f), 0.2f), width * 0.7f, width * 0.3f, life * 0.7f, FxColors.White);
        }

        /// <summary>An electric zig-zag between two points.</summary>
        public void Jagged(Vector3 a, Vector3 b, Color c, float width, float life, int segments, float amp)
        {
            pts.Clear();
            var side = Vector3.Cross((b - a).normalized, Vector3.up);
            for (int i = 0; i <= segments; i++)
            {
                float f = i / (float)segments;
                var p = Vector3.Lerp(a, b, f);
                if (i > 0 && i < segments) p += side * Random.Range(-amp, amp) + Up(Random.Range(-amp, amp) * 0.6f);
                pts.Add(p);
            }
            pool.Line(pts, FxColors.Hot(c, 1.3f), width * 2.4f, width, life);
            pool.Line(pts, FxColors.Hot(FxColors.White, 1.1f), width * 0.7f, width * 0.3f, life * 0.7f);
        }

        /// <summary>Flames thrown from a to b (flamers, Flamer tower).</summary>
        public void Flame(Vector3 a, Vector3 b, float width, float life = 0.35f)
        {
            var d = b - a;
            float dist = d.magnitude;
            if (dist < 0.01f) return;
            var dir = d / dist;
            pool.Glow(a + dir * 0.4f, FxColors.Hot(FxColors.Orange, 1.4f), 0.9f * width, 1.4f * width, 0.12f);
            for (int i = 0; i < 6; i++)     // a stream of fire puffs that swell and cool from yellow to red
            {
                float k = i / 5f;
                var c = Color.Lerp(new Color(1f, 0.85f, 0.4f), new Color(1f, 0.35f, 0.08f), k);
                var p = pool.Glow(a + dir * 0.3f, FxColors.Hot(c, 1.5f), 0.5f * width, (2.4f + 1.2f * k) * width, life * Random.Range(0.75f, 1f));
                var j = Random.insideUnitSphere * 0.22f;
                j.y = Mathf.Abs(j.y);
                p.vel = (dir + j).normalized * (dist / life * 1.25f);
                p.drag = 1.6f;
                p.ease = 1.5f;
                p.delay = i * 0.025f;
                p.flicker = 0.25f;
            }
            pool.Motes(a + dir * 0.3f, FxColors.Hot(new Color(1f, 0.6f, 0.2f), 1.4f), 4, dist / life * 1.4f, life, 0.35f * width, dir, 0.25f);
            if (Random.value < 0.5f) pool.Smoke(b + Up(0.4f), FxColors.A(FxColors.Smoke, 0.45f), 1, 0.8f, 0.9f, 1.0f * width);
        }

        // ------------------------------------------------------------------ unit and tower shots
        public void UnitShot(Unit u, Vector3 muzzle, Vector3 target, int count)
        {
            bool player = u.faction == "player";
            var col = player ? FxColors.PlayerTracer : FxColors.AlienTracer;
            if (u.burn)
            {
                Flame(muzzle, target, 1f + u.radius * 0.02f);
                return;
            }
            var dir = (target - muzzle).normalized;
            bool sniper = u.kind == "snipers";
            float width = sniper ? 0.2f : u.cls == "inf" || u.cls == "hero" ? 0.13f : 0.18f;
            float spread = Mathf.Min(1.4f, 0.25f + u.radius * 0.03f);
            for (int i = 0; i < Mathf.Max(1, count); i++)
            {
                var off = count > 1 ? RandFlat(spread) : Vector3.zero;
                var a = muzzle + off;
                var b = target + Random.insideUnitSphere * 0.45f;
                Tracer(a, b, sniper ? FxColors.Cryo : col, width, sniper ? 0.16f : 0.07f);
                if (i < 2) MuzzleFlash(a + dir * 0.15f, dir, player ? new Color(1f, 0.8f, 0.5f) : FxColors.AlienGreen, 0.45f + Mathf.Min(1.6f, u.radius * 0.04f));
            }
            if (Random.value < 0.6f) pool.Sparks(target, col, 2, 5f, 0.18f, 0.07f);
        }

        public void TowerShot(Tower t, Vector3 muzzle, Vector3 target, int count)
        {
            switch (t.behavior)
            {
                case "drones":        // little drones around the tower each fire a green tracer (sim.py drones)
                    for (int i = 0; i < Mathf.Max(1, count); i++)
                    {
                        var d = muzzle + RandFlat(1.4f) + Up(Random.Range(0.6f, 1.6f));
                        pool.Glow(d, FxColors.Hot(FxColors.Green, 1.4f), 1.0f, 1.2f, 0.12f);
                        Tracer(d, target + Random.insideUnitSphere * 0.4f, FxColors.Green, 0.12f, 0.08f);
                    }
                    break;
                case "fence":         // shock fence arcs (cyan, 0.12 s)
                    Jagged(muzzle, target, FxColors.Cyan, 0.06f, 0.12f, 5, 0.35f);
                    pool.Sparks(target, FxColors.Cyan, 2, 4f, 0.15f, 0.06f);
                    break;
                case "chrono":
                    Tracer(muzzle, target, FxColors.Energy, 0.2f, 0.3f);
                    break;
                case "hacker":
                    Jagged(muzzle, target, FxColors.Magenta, 0.06f, 0.3f, 7, 0.25f);
                    break;
                case "cone":          // Flamer tower: flames in a cone towards the target
                    Flame(muzzle, target, 1.5f, 0.35f);
                    break;
                case "acid":          // Acid tower lobs a glob (acid tracer, 0.2 s)
                {
                    pts.Clear();
                    for (int i = 0; i <= 8; i++)
                    {
                        float f = i / 8f;
                        pts.Add(Vector3.Lerp(muzzle, target, f) + Up(Mathf.Sin(f * Mathf.PI) * 2.2f));
                    }
                    pool.Line(pts, FxColors.Hot(FxColors.Acid, 1.2f), 0.18f, 0.08f, 0.22f);
                    pool.Glow(muzzle, FxColors.Acid, 0.8f, 1.1f, 0.12f);
                    break;
                }
                case "garrison":      // Bunker: rifles from the firing ports
                {
                    var dir = (target - muzzle).normalized;
                    for (int i = 0; i < Mathf.Max(1, count); i++)
                    {
                        var a = muzzle + RandFlat(1.0f);
                        Tracer(a, target + Random.insideUnitSphere * 0.4f, FxColors.PlayerTracer, 0.13f, 0.07f);
                        MuzzleFlash(a, dir, new Color(1f, 0.8f, 0.5f), 0.55f);
                    }
                    break;
                }
                default:
                    Tracer(muzzle, target, t.faction == "player" ? FxColors.PlayerTracer : FxColors.AlienTracer, 0.14f, 0.08f);
                    break;
            }
        }

        /// <summary>The moment a tower acts (IBattleFx.TowerFired): muzzle flashes and the looks that belong to the tower itself.</summary>
        public void TowerFired(Tower t, Vector3 muzzle, Vector3 targetPos, Vector3 ground)
        {
            var dir = targetPos - muzzle;
            dir = dir.sqrMagnitude > 1e-4f ? dir.normalized : Vector3.forward;
            float rng = t.range * t.buffRange * MapBuilder.Unit;
            switch (t.behavior)
            {
                case "projectile":
                    MuzzleFlash(muzzle + dir * 0.6f, dir, t.kind == "spine" ? FxColors.Acid : FxColors.Cyan, 0.9f);
                    break;
                case "pierce":
                    MuzzleFlash(muzzle + dir * 0.8f, dir, FxColors.Cyan, 1.6f);
                    break;
                case "missile":
                case "flak":
                    MuzzleFlash(muzzle + dir * 0.6f, dir, FxColors.Orange, 1.1f);
                    pool.Smoke(muzzle, FxColors.A(FxColors.Smoke, 0.5f), 2, 1.2f, 0.9f, 0.9f);
                    break;
                case "mortar":
                    MuzzleFlash(muzzle + Up(0.6f), Vector3.up, FxColors.Orange, 1.6f);
                    pool.Smoke(muzzle + Up(0.6f), FxColors.A(FxColors.Smoke, 0.6f), 4, 2.0f, 1.3f, 1.2f);
                    break;
                case "mines":
                    pool.Smoke(muzzle, FxColors.A(FxColors.Dust, 0.5f), 2, 1.0f, 0.7f, 0.7f);
                    break;
                case "chain":
                case "lightning":
                {
                    var c = t.behavior == "chain" ? FxColors.Cyan : FxColors.Energy;
                    pool.Glow(muzzle + Up(0.4f), FxColors.Hot(c, 1.6f), 1.4f, 2.2f, 0.15f);
                    pool.Sparks(muzzle + Up(0.4f), c, 4, 6f, 0.2f, 0.07f);
                    break;
                }
                case "ion":
                    pool.Glow(muzzle + Up(0.5f), FxColors.Hot(FxColors.Cyan, 2f), 2.2f, 3.2f, 0.3f);
                    pool.Flat(FxTex.Ring, FxBlend.Add, ground + Up(0.15f), FxColors.Hot(FxColors.Cyan, 1.3f), 1.5f, 5f, 0.35f);
                    break;
                case "beam":
                    pool.Glow(muzzle + dir * 0.5f, FxColors.Hot(FxColors.Red, 1.4f), 0.7f, 0.8f, 0.1f);
                    break;
                case "acid":
                    pool.Motes(muzzle, FxColors.Acid, 3, 3f, 0.3f, 0.2f, dir, 0.6f);
                    break;
                case "shockwave":
                    Shockwave(ground, rng);
                    break;
                case "chrono":
                    pool.Glow(muzzle + Up(0.4f), FxColors.Hot(FxColors.Energy, 1.5f), 1.6f, 2.4f, 0.3f);
                    break;
                case "hacker":
                    pool.Glow(muzzle + Up(0.4f), FxColors.Hot(FxColors.Magenta, 1.5f), 1.6f, 2.4f, 0.3f);
                    break;
                case "brood":
                    GooBurst(ground + Up(0.4f), 1.2f, false);
                    break;
                case "aura":
                case "gravity":
                case "tar":
                case "magnet":
                case "fence":
                    AuraPulse(t, ground, rng);
                    break;
            }
        }

        /// <summary>Slow towers (Cryo, Gravity, Tar, Magnet, Fence) pulse while enemies are in range, about once a second.</summary>
        public void AuraPulse(Tower t, Vector3 ground, float rng)
        {
            float next;
            if (pulseNext.TryGetValue(t, out next) && clock < next) return;
            pulseNext[t] = clock + 1f;
            var g = ground + Up(0.14f);
            float d = rng * 2f / 0.86f;     // the ring texture peaks at 0.86 of the quad's half size
            switch (t.behavior)
            {
                case "aura":       // Cryo: frost ring spreading out, snow motes
                    pool.Flat(FxTex.Ring, FxBlend.Add, g, FxColors.Hot(FxColors.Cryo, 1.2f), d * 0.2f, d, 0.9f);
                    pool.Flat(FxTex.Glow, FxBlend.Add, g, FxColors.A(FxColors.Cryo, 0.35f), d * 0.5f, d * 0.9f, 0.9f).fadeIn = 0.2f;
                    for (int i = 0; i < 6; i++) pool.Motes(g + RandFlat(rng) + Up(0.5f), FxColors.White, 1, 0.4f, 1.2f, 0.25f, Vector3.up, 1f);
                    break;
                case "gravity":    // Gravity well: violet ring pulled inwards, swirling motes
                {
                    var r = pool.Flat(FxTex.Ring, FxBlend.Add, g, FxColors.Hot(FxColors.Violet, 1.3f), d, d * 0.15f, 0.8f);
                    r.ease = 1.2f;
                    r.fadeIn = 0.15f;
                    for (int i = 0; i < 8; i++)
                    {
                        var p = g + RandFlat(rng) + Up(0.4f);
                        var m = pool.Glow(p, FxColors.Hot(FxColors.Violet, 1.2f), 0.3f, 0.1f, 0.8f);
                        m.vel = (g + Up(0.4f) - p) * 1.1f;
                    }
                    break;
                }
                case "tar":        // Tar pit: dark sticky patch
                    pool.Flat(FxTex.Disc, FxBlend.Alpha, g - Up(0.06f), FxColors.Tar, d * 0.75f, d * 0.8f, 1.2f).fadeIn = 0.3f;
                    pool.Chunks(g, FxColors.Tar, 3, 2f, 0.6f, 0.25f);
                    break;
                case "magnet":     // Magnet: blue-violet field rings
                    pool.Flat(FxTex.Ring, FxBlend.Add, g, FxColors.Hot(new Color(0.45f, 0.5f, 1f), 1.2f), d * 0.3f, d, 0.7f);
                    pool.Flat(FxTex.Ring, FxBlend.Add, g, FxColors.Hot(new Color(0.45f, 0.5f, 1f), 1.0f), d * 0.15f, d * 0.7f, 0.7f).delay = 0.2f;
                    break;
                default:           // Fence: crackle on the posts
                    pool.Glow(g + Up(1.2f), FxColors.Hot(FxColors.Cyan, 1.4f), 1.0f, 1.4f, 0.12f);
                    pool.Sparks(g + Up(1.2f), FxColors.Cyan, 3, 4f, 0.2f, 0.06f);
                    break;
            }
        }

        /// <summary>Sonic tower: a stunning ring out to its range (Python: CRYO ring, 0.35 s).</summary>
        public void Shockwave(Vector3 ground, float rng)
        {
            float d = rng * 2f / 0.86f;
            var g = ground + Up(0.18f);
            var r = pool.Flat(FxTex.Ring, FxBlend.Add, g, FxColors.Hot(FxColors.Cryo, 1.6f), d * 0.1f, d, 0.35f);
            r.ease = 1.5f;
            var r2 = pool.Flat(FxTex.Ring, FxBlend.Add, g, FxColors.Hot(FxColors.White, 1.0f), d * 0.05f, d * 0.8f, 0.35f);
            r2.delay = 0.07f;
            pool.Glow(g + Up(0.8f), FxColors.Hot(FxColors.Cryo, 1.5f), 1.5f, 3f, 0.2f);
            for (int i = 0; i < 10; i++)
            {
                float a = i / 10f * Mathf.PI * 2f;
                pool.Smoke(g + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * rng * 0.5f, FxColors.A(FxColors.Dust, 0.35f), 1, 1.2f, 0.7f, 1.2f, 0.2f);
            }
        }

        // ------------------------------------------------------------------ beams and lightning
        /// <summary>Railgun (pierce), Ion cannon and Laser (beam) looks; t may be null for an unknown source.</summary>
        public void Beam(Tower t, Vector3 a, Vector3 b)
        {
            string behavior = t != null ? t.behavior : "pierce";
            switch (behavior)
            {
                case "ion":          // fat cyan beam + a column from the sky at the target (0.4 s)
                    pool.Line(a, b, FxColors.Hot(FxColors.Cyan, 1.4f), 1.1f, 0.2f, 0.4f);
                    pool.Line(a, b, FxColors.Hot(FxColors.White, 1.3f), 0.35f, 0.05f, 0.3f);
                    pool.Line(b + Up(28f), b, FxColors.A(FxColors.Hot(FxColors.Cyan, 1.2f), 0.1f), 1.6f, 0.3f, 0.4f, FxColors.Hot(FxColors.Cyan, 1.4f));
                    pool.Glow(b, FxColors.Hot(FxColors.White, 2f), 2.5f, 4f, 0.15f);
                    break;
                case "beam":         // Laser: red, thicker and whiter as it ramps up on one target
                {
                    float k = 0f;
                    if (t != null) k = Mathf.Clamp01(t.beamT / Mathf.Max(0.1f, GameData.F(t.stats, "ramp_time", 3f)));
                    var c = Color.Lerp(FxColors.Red, new Color(1f, 0.7f, 0.6f), k * 0.6f);
                    pool.Line(a, b, FxColors.Hot(c, 1.3f + k), 0.14f + 0.3f * k, 0.1f + 0.25f * k, 0.22f);
                    pool.Line(a, b, FxColors.Hot(FxColors.White, 1f), 0.04f + 0.08f * k, 0.03f, 0.18f);
                    pool.Glow(b, FxColors.Hot(c, 1.5f), 0.8f + 1.2f * k, 1.0f + 1.2f * k, 0.15f);
                    if (Random.value < 0.4f + k * 0.4f) pool.Sparks(b, c, 2, 5f, 0.25f, 0.07f);
                    break;
                }
                default:              // Railgun: cyan slug line with a white core (0.18 s)
                    pool.Line(a, b, FxColors.Hot(FxColors.Cyan, 1.4f), 0.55f, 0.05f, 0.25f);
                    pool.Line(a, b, FxColors.Hot(FxColors.White, 1.4f), 0.16f, 0.02f, 0.18f);
                    for (int i = 1; i <= 5; i++)
                        pool.Motes(Vector3.Lerp(a, b, i / 6f), FxColors.Cyan, 1, 0.8f, 0.5f, 0.3f);
                    break;
            }
        }

        /// <summary>Tesla (chain, cyan) and Lightning (yellow) arcs through the hit enemies.</summary>
        public void Lightning(IList<Vector3> nodes, Color c)
        {
            if (nodes.Count < 2) return;
            pts.Clear();
            pts.Add(nodes[0]);
            for (int i = 1; i < nodes.Count; i++)
            {
                var a = nodes[i - 1];
                var b = nodes[i];
                float len = (b - a).magnitude;
                var side = Vector3.Cross((b - a).normalized, Vector3.up);
                for (int k = 1; k < 6; k++)
                    pts.Add(Vector3.Lerp(a, b, k / 6f) + side * Random.Range(-0.12f, 0.12f) * len + Up(Random.Range(-0.08f, 0.08f) * len));
                pts.Add(b);
            }
            pool.Line(pts, FxColors.Hot(c, 1.4f), 0.5f, 0.25f, 0.22f);
            pool.Line(pts, FxColors.Hot(FxColors.White, 1.2f), 0.14f, 0.06f, 0.15f);
            int n = nodes.Count;
            for (int i = 1; i < n; i++)
            {
                var node = nodes[i];
                pool.Glow(node, FxColors.Hot(c, 1.5f), 1.4f, 1.8f, 0.18f);
                if (Random.value < 0.6f) Jagged(node, node + Random.onUnitSphere * 1.6f, c, 0.05f, 0.12f, 3, 0.3f);   // a short fork
            }
            pool.Sparks(nodes[n - 1], c, 5, 6f, 0.25f, 0.07f);
        }

        /// <summary>Salvo / Barrage rockets coming in (Python: orange tracer, 0.1 s).</summary>
        public void StrikeTrail(Vector3 from, Vector3 to, string faction)
        {
            var c = faction == "player" ? FxColors.Orange : FxColors.AlienGreen;
            var a = Vector3.Lerp(from, to, 0.55f) + Up(5f);
            pool.Line(a, to, FxColors.A(FxColors.Hot(c, 1.2f), 0.1f), 0.08f, 0.25f, 0.14f, FxColors.Hot(c, 1.4f));
            pool.Smoke(Vector3.Lerp(a, to, 0.5f), FxColors.A(FxColors.Smoke, 0.35f), 1, 0.4f, 0.8f, 0.6f);
        }

        // ------------------------------------------------------------------ projectiles
        /// <summary>What a projectile in flight looks like; Fx moves it every frame.</summary>
        public class ShotView
        {
            public Projectile p;
            public FxPiece head, core;
            public float trail, age;
            public bool shown;
            public Color color;
        }

        public ShotView ProjectileStart(Projectile p, Vector3 at)
        {
            var v = new ShotView { p = p };
            bool alien = p.faction != "player";
            Color glow, core;
            float size;
            switch (p.kind)
            {
                case "bolt": glow = new Color(0.24f, 0.63f, 0.86f); core = FxColors.Cyan; size = 0.9f; break;
                case "acid": glow = new Color(0.4f, 0.8f, 0.16f); core = FxColors.Acid; size = 1.0f; break;
                case "rocket": glow = new Color(0.9f, 0.55f, 0.24f); core = new Color(1f, 0.9f, 0.63f); size = 0.9f; break;
                case "flak": glow = new Color(0.9f, 0.55f, 0.24f); core = new Color(1f, 0.9f, 0.63f); size = 0.6f; break;
                case "lob":
                    glow = alien ? new Color(0.35f, 0.75f, 0.2f) : new Color(0.9f, 0.55f, 0.24f);
                    core = alien ? FxColors.AlienGreen : FxColors.Orange;
                    size = 1.2f;
                    break;
                case "shell": glow = new Color(0.8f, 0.7f, 0.3f); core = FxColors.Energy; size = 1.0f; break;
                default: glow = new Color(0.78f, 0.7f, 0.31f); core = FxColors.Energy; size = 0.8f; break;
            }
            v.color = glow;
            size *= 1.5f;
            v.head = pool.Glow(at, FxColors.Hot(glow, 1.3f), size, size, 1f);
            v.head.held = true;
            v.core = pool.Glow(at, FxColors.Hot(core, 1.5f), size * 0.4f, size * 0.4f, 1f);
            v.core.held = true;
            v.shown = p.delay <= 0;
            if (!v.shown) { v.head.color.a = 0; v.core.color.a = 0; }
            return v;
        }

        public void ProjectileMove(ShotView v, Vector3 pos, float dt)
        {
            v.age += dt;
            bool visible = v.p.delay <= 0;
            if (visible && !v.shown)
            {
                v.shown = true;
                v.head.color.a = v.core.color.a = 1f;
                MuzzleFlash(pos, Vector3.up, v.color, 0.9f);   // a later shot of a burst leaves now
            }
            var step = pos - v.head.pos;
            v.head.pos = v.core.pos = pos;
            if (!visible) return;
            v.trail -= dt;
            if (v.trail > 0) return;
            switch (v.p.kind)
            {
                case "rocket":
                case "flak":
                    v.trail = 0.03f;
                    pool.Smoke(pos - step * 0.5f, FxColors.A(new Color(0.47f, 0.46f, 0.44f), 0.55f), 1, 0.3f, 0.55f, 0.55f, 0.05f);
                    pool.Motes(pos, FxColors.Hot(FxColors.Orange, 1.2f), 1, 0.5f, 0.12f, 0.3f);
                    break;
                case "lob":
                    v.trail = 0.06f;
                    if (Random.value < 0.4f) pool.Smoke(pos, FxColors.A(v.p.faction == "player" ? new Color(0.27f, 0.25f, 0.23f) : new Color(0.27f, 0.35f, 0.2f), 0.5f), 1, 0.2f, 0.6f, 0.45f, 0.05f);
                    break;
                case "acid":
                    v.trail = 0.05f;
                    pool.Motes(pos, FxColors.Acid, 1, 0.3f, 0.25f, 0.25f);
                    break;
                default:
                    v.trail = 0.04f;
                    pool.Motes(pos, v.color, 1, 0.2f, 0.15f, 0.22f);
                    break;
            }
        }

        public void ProjectileEnd(ShotView v)
        {
            pool.Release(v.head);
            pool.Release(v.core);
            v.head = v.core = null;
        }

        // ------------------------------------------------------------------ hits, explosions, deaths
        /// <summary>A shot landing. size is the splash radius in game pixels (10 or 6 for single-target hits).
        /// hint names what landed (projectile kind or tower behaviour) to pick the look.</summary>
        public void Impact(Vector3 at, Vector3 ground, string faction, float size, bool air, string hint)
        {
            bool alien = faction != "player";
            var c = alien ? FxColors.AlienGreen : FxColors.Orange;
            if (hint == "acid") c = FxColors.Acid;
            else if (hint == "bolt" || hint == "ion") c = FxColors.Cyan;
            if (size <= 12f)
            {
                pool.Glow(at, FxColors.Hot(c, 1.4f), 0.9f, 1.3f, 0.1f);
                pool.Sparks(at, c, 4, 6f, 0.25f, 0.07f);
                if (hint == "acid") pool.Chunks(at, FxColors.Goo, 4, 3f, 0.4f, 0.18f);
                return;
            }
            float radius = Mathf.Clamp(size * 0.06f, 0.6f, 5f);
            if (hint == "acid" || (alien && hint == "lob"))
            {
                GooBurst(air ? at : ground + Up(0.3f), radius, !air);
                return;
            }
            Explosion(air ? at : ground, c, radius, !air, hint == "mines");
        }

        /// <summary>A fireball with a flash, shock ring, sparks, smoke, debris and (on the ground) a scorch mark.</summary>
        public void Explosion(Vector3 ground, Color c, float radius, bool scorch = true, bool dirt = false)
        {
            var mid = ground + Up(0.4f + radius * 0.35f);
            pool.Glow(mid, FxColors.Hot(FxColors.White, 2f), radius * 1.5f, radius * 2.4f, 0.12f);
            var fire = pool.Glow(mid + Up(radius * 0.2f), FxColors.Hot(c, 1.5f), radius * 1.3f, radius * 2.8f, 0.45f);
            fire.ease = 3f;
            fire.vel = Up(radius * 0.8f);
            var fire2 = pool.Glow(mid + RandFlat(radius * 0.4f), FxColors.Hot(Color.Lerp(c, new Color(0.7f, 0.2f, 0.05f), 0.4f), 1.3f), radius * 1.0f, radius * 2.2f, 0.5f);
            fire2.delay = 0.05f;
            fire2.vel = Up(radius * 1.2f);
            pool.Flat(FxTex.Ring, FxBlend.Add, ground + Up(0.12f), FxColors.Hot(Color.Lerp(c, FxColors.White, 0.5f), 1.2f), radius * 0.6f, radius * 3.4f, 0.35f).ease = 2.5f;
            pool.Sparks(mid, c, Mathf.Min(40, 8 + (int)(radius * 5)), 6f + radius * 4f, 0.6f, 0.16f);
            pool.Smoke(mid, FxColors.Smoke, Mathf.Min(14, 3 + (int)(radius * 2.5f)), 1f + radius * 0.8f, 1.8f, radius * 1.4f, radius * 0.4f);
            pool.Chunks(ground + Up(0.3f), dirt ? FxColors.Dirt : FxColors.Debris, Mathf.Min(16, 3 + (int)(radius * 3)), 5f + radius * 2.5f, 0.9f, 0.22f);
            if (scorch)
            {
                var s = pool.Flat(FxTex.Disc, FxBlend.Alpha, ground + Up(0.05f), FxColors.Scorch, radius * 2.2f, radius * 2.4f, 8f);
                s.fadeIn = 0.01f;
                s.fadeOut = 0.4f;
            }
        }

        /// <summary>Alien guts: green goo chunks, a splat on the ground, glowing motes.</summary>
        public void GooBurst(Vector3 at, float radius, bool splat)
        {
            pool.Glow(at, FxColors.Hot(FxColors.AlienGreen, 1.3f), radius * 1.8f, radius * 3f, 0.3f);
            var puff = pool.Glow(at, FxColors.A(new Color(0.3f, 0.6f, 0.15f), 0.8f), radius * 1.2f, radius * 2.6f, 0.5f);
            puff.ease = 3f;
            pool.Chunks(at, FxColors.Goo, Mathf.Min(20, 8 + (int)(radius * 4)), 4f + radius * 2.5f, 0.8f, 0.35f + radius * 0.06f);
            pool.Motes(at, FxColors.Hot(FxColors.AlienGreen, 1.2f), 8, 3f + radius * 1.5f, 0.6f, 0.4f);
            if (splat)
            {
                var s = pool.Flat(FxTex.Smoke, FxBlend.Alpha, new Vector3(at.x, at.y - 0.25f, at.z), FxColors.A(new Color(0.22f, 0.45f, 0.1f), 0.85f), radius * 1.6f, radius * 2.8f, 6f);
                s.ease = 6f;
                s.fadeOut = 0.4f;
            }
        }

        /// <summary>Death effects by size and faction (Python: explosion 14 / 26 / 24 / 40, goo for aliens).</summary>
        public void Death(Entity e, Vector3 at, Vector3 ground)
        {
            bool alien = e.faction != "player";
            var c = alien ? FxColors.AlienGreen : FxColors.Orange;
            var u = e as Unit;
            if (u != null)
            {
                float r = u.cls == "inf" || u.cls == "hero" ? 1.1f : Mathf.Clamp(u.radius * 0.1f, 1.4f, 3.2f);
                if (alien && !e.flying) GooBurst(ground + Up(0.4f), r, true);
                else if (alien) { GooBurst(at, r, false); pool.Smoke(at, FxColors.A(FxColors.Smoke, 0.5f), 4, 1f, 1.4f, r); }
                else Explosion(e.flying ? at : ground, c, r, !e.flying, false);
                if (e.flying)       // the wreck falls trailing fire
                {
                    var w = pool.Glow(at, FxColors.Hot(c, 1.2f), r * 0.8f, r * 0.3f, 0.7f);
                    w.vel = new Vector3(Random.Range(-1f, 1f), -7f, Random.Range(-1f, 1f));
                }
                return;
            }
            float size = e is Tower ? 2.4f : 4f;
            if (alien)
            {
                GooBurst(ground + Up(0.8f), size, true);
                Explosion(ground, FxColors.AlienGreen, size * 0.8f, false, true);
            }
            else Explosion(ground, c, size, true, false);
            if (!(e is Tower))      // buildings: a few more blasts across the footprint
            {
                float rr = e.radius * MapBuilder.Unit;
                pool.Repeat(0.7f, 0.18f, age =>
                {
                    if (age <= 0f) return;
                    if (alien) GooBurst(ground + RandFlat(rr) + Up(0.6f), size * 0.5f, false);
                    else Explosion(ground + RandFlat(rr), c, size * 0.5f, false, false);
                });
                pool.Smoke(ground + Up(1f), FxColors.Smoke, 10, 2f, 3f, size * 1.2f, rr * 0.6f);
            }
        }

        // ------------------------------------------------------------------ patches
        /// <summary>A burning (Napalm) or acid (Acid tower) patch for as long as it lasts. r in world units.</summary>
        public void Zone(Vector3 ground, float r, float life, string kind)
        {
            var g = ground + Up(0.06f);
            float d = r * 2f;
            if (kind == "fire")
            {
                var s = pool.Flat(FxTex.Disc, FxBlend.Alpha, g, FxColors.Scorch, d, d * 1.05f, life + 4f);
                s.fadeIn = 0.05f;
                s.fadeOut = 4f / (life + 4f);
                var glow = pool.Flat(FxTex.Glow, FxBlend.Add, g + Up(0.04f), FxColors.Hot(FxColors.Orange, 1.4f), d * 1.1f, d * 1.15f, life);
                glow.flicker = 0.35f;
                glow.fadeIn = 0.08f;
                glow.fadeOut = 0.25f;
                pool.Repeat(life, 0.06f, age =>
                {
                    var p = g + RandFlat(r * 0.9f);
                    pool.Motes(p, FxColors.Hot(new Color(1f, 0.5f, 0.12f), 1.5f), 1, 2.2f, 0.6f, 1.4f, Vector3.up, 0.35f);
                    if (Random.value < 0.3f) pool.Motes(p, FxColors.Hot(new Color(1f, 0.85f, 0.4f), 1.3f), 1, 3f, 0.4f, 0.35f, Vector3.up, 0.3f);
                    if (Random.value < 0.15f) pool.Smoke(p + Up(1.2f), FxColors.A(FxColors.Smoke, 0.4f), 1, 0.8f, 1.6f, 1.2f);
                });
            }
            else if (kind == "acid")
            {
                var s = pool.Flat(FxTex.Smoke, FxBlend.Alpha, g, FxColors.A(new Color(0.32f, 0.6f, 0.12f), 0.75f), d * 0.7f, d * 1.15f, life);
                s.ease = 5f;
                s.fadeOut = 0.3f;
                var ring = pool.Flat(FxTex.Glow, FxBlend.Add, g + Up(0.03f), FxColors.A(FxColors.Acid, 0.35f), d * 0.7f, d * 1.1f, life);
                ring.ease = 5f;
                ring.fadeOut = 0.3f;
                pool.Repeat(life, 0.18f, age =>
                {
                    var p = g + RandFlat(r * 0.85f);
                    pool.Motes(p, FxColors.Hot(FxColors.Acid, 1.2f), 1, 0.8f, 0.7f, 0.5f, Vector3.up, 0.3f);
                    if (Random.value < 0.25f) pool.Smoke(p + Up(0.3f), FxColors.A(new Color(0.45f, 0.6f, 0.25f), 0.3f), 1, 0.4f, 1.2f, 0.9f);
                });
            }
            else
            {
                var s = pool.Flat(FxTex.Disc, FxBlend.Alpha, g, FxColors.A(FxColors.Magenta, 0.25f), d, d, life);
                s.fadeOut = 0.3f;
            }
        }
    }
}
