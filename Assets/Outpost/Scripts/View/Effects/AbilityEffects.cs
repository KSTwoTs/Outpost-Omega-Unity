// AbilityEffects.cs · roadmap step M1.5 · combat effects (see ARCHITECTURE.md).
// One proper visual per unit ability (frag, canister, salvo, barrage, napalm, stim, frenzy, lockdown, ram, charge,
// jetpack, stomp, camo, stim_field, burrow, rally, brood), the Sensor Array Scan, the generic ground rings the Sim
// reports (dash landings, upgrades, tower stasis / hack), and a hook for leader / alien powers (step 6).
// Colours follow the Python game (sim.py use_ability). Everything is drawn with EffectPool.

using UnityEngine;
using Outpost.Content;
using Outpost.Sim;

namespace Outpost.View
{
    public class AbilityEffects
    {
        readonly EffectPool pool;
        readonly WeaponEffects weapons;

        public AbilityEffects(EffectPool pool, WeaponEffects weapons)
        {
            this.pool = pool;
            this.weapons = weapons;
        }

        static Vector3 Up(float y) => new Vector3(0, y, 0);
        static Vector3 RandFlat(float r) { var c = Random.insideUnitCircle * r; return new Vector3(c.x, 0, c.y); }
        static float RingSize(float r) => r * 2f / 0.86f;     // quad size for a ring of radius r (texture peak at 0.86)

        FxPiece GroundRing(Vector3 g, Color c, float r0, float r1, float life, float hot = 1.3f)
        {
            // big rings get a wide band from the texture; keep them fainter so they do not swamp the battle
            c.a *= Mathf.Clamp(5f / Mathf.Max(0.1f, Mathf.Max(r0, r1)), 0.35f, 1f);
            var p = pool.Flat(FxTex.Ring, FxBlend.Add, g + Up(0.15f), FxColors.Hot(c, hot), RingSize(r0), RingSize(r1), life);
            p.ease = 2.5f;
            return p;
        }

        /// <summary>A unit used its ability (IBattleFx.Ability). from = the unit (muzzle height), to = the aimed point on
        /// the ground, radius in world units (0 when the ability has none).</summary>
        public void Use(Unit u, string id, Vector3 from, Vector3 ground, Vector3 to, float radius)
        {
            bool player = u.faction == "player";
            float body = Mathf.Max(1.2f, u.radius * MapBuilder.Unit);
            switch (id)
            {
                case "frag":
                case "canister":          // the throw: a puff at the hand (the grenade itself is a lob projectile)
                    weapons.MuzzleFlash(from, (to - from).normalized, FxColors.Orange, id == "canister" ? 1.4f : 0.7f);
                    pool.Smoke(from, FxColors.A(FxColors.Smoke, 0.4f), 2, 0.8f, 0.7f, 0.6f);
                    GroundRing(to, FxColors.Red, radius, radius * 0.85f, 0.7f, 1.0f).fadeIn = 0.2f;
                    break;
                case "salvo":             // four rockets leave the Hornet; red target rings where they will land
                    for (int i = 0; i < 4; i++)
                    {
                        var g = pool.Glow(from + Random.insideUnitSphere * 0.4f, FxColors.Hot(FxColors.Orange, 1.6f), 0.9f, 1.3f, 0.1f);
                        g.delay = i * 0.15f;
                        var r = GroundRing(to + RandFlat(2.5f), FxColors.Red, radius, radius * 0.7f, 0.35f + i * 0.15f, 1.1f);
                        r.fadeIn = 0.15f;
                    }
                    pool.Smoke(from, FxColors.A(FxColors.Smoke, 0.45f), 4, 1.2f, 1f, 0.8f);
                    break;
                case "barrage":           // six shells over 1.4 s: a warning reticle over the area
                {
                    float r = 55f * MapBuilder.Unit + 6f;
                    var ring = GroundRing(to, FxColors.Red, r, r * 0.9f, 1.5f, 1.2f);
                    ring.fadeIn = 0.1f;
                    ring.fadeOut = 0.3f;
                    ring.spin = 40f;
                    var star = pool.Flat(FxTex.Star, FxBlend.Add, to + Up(0.2f), FxColors.Hot(FxColors.Red, 1.2f), r * 0.6f, r * 0.5f, 1.5f);
                    star.spin = -60f;
                    star.fadeOut = 0.3f;
                    weapons.MuzzleFlash(from + Up(0.5f), Vector3.up, FxColors.Orange, 1.6f);
                    pool.Smoke(from + Up(0.5f), FxColors.A(FxColors.Smoke, 0.55f), 5, 1.6f, 1.4f, 1.1f);
                    break;
                }
                case "napalm":            // a burning streak to the spot (Python: orange tracer 0.25 s); the patch is a Zone
                {
                    pool.Line(from, to + Up(0.4f), FxColors.A(FxColors.Hot(FxColors.Orange, 1.3f), 0.25f), 0.25f, 0.5f, 0.3f, FxColors.Hot(FxColors.Orange, 1.5f));
                    for (int i = 1; i <= 6; i++)
                        pool.Motes(Vector3.Lerp(from, to, i / 7f) + Up(Mathf.Sin(i / 7f * Mathf.PI) * 1.2f), FxColors.Hot(new Color(1f, 0.55f, 0.15f), 1.4f), 2, 1.5f, 0.45f, 0.5f);
                    break;
                }
                case "stim":
                case "frenzy":            // red pulse and rising motes (Python: red ring)
                {
                    var c = id == "stim" ? FxColors.Red : new Color(0.95f, 0.35f, 0.15f);
                    GroundRing(ground, c, body * 0.6f, body + 1.4f, 0.45f);
                    pool.Motes(ground + Up(0.6f), FxColors.Hot(c, 1.3f), 10, 2.5f, 0.6f, 0.25f, Vector3.up, 0.8f, body * 0.5f);
                    pool.Glow(ground + Up(1f), FxColors.Hot(c, 1.2f), body * 1.4f, body * 2f, 0.25f);
                    break;
                }
                case "lockdown":          // the Missile Truck braces: cyan anchor ring and bolts into the ground
                    GroundRing(ground, FxColors.Cyan, body * 1.6f, body * 1.1f, 0.6f, 1.4f);
                    for (int i = 0; i < 4; i++)
                    {
                        float a = i * Mathf.PI * 0.5f + Mathf.PI * 0.25f;
                        var p = ground + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * body * 1.1f;
                        pool.Glow(p + Up(0.2f), FxColors.Hot(FxColors.Cyan, 1.4f), 0.6f, 0.9f, 0.4f);
                        pool.Chunks(p, FxColors.Dust, 3, 2.5f, 0.4f, 0.15f);
                    }
                    break;
                case "ram":
                case "charge":            // dust kicked up and speed streaks along the dash
                {
                    var dir = to - ground;
                    dir.y = 0;
                    var n = dir.sqrMagnitude > 1e-3f ? dir.normalized : Vector3.forward;
                    pool.Smoke(ground + Up(0.3f), FxColors.Dust, 6, 2f, 0.9f, body * 0.9f, body * 0.5f);
                    pool.Chunks(ground, FxColors.Dirt, 6, 4f, 0.5f, 0.18f);
                    var side = Vector3.Cross(n, Vector3.up);
                    for (int i = -1; i <= 1; i++)
                    {
                        var a = ground + Up(0.6f + 0.3f * Mathf.Abs(i)) + side * i * body * 0.5f;
                        pool.Line(a, a + n * (2.5f + body), FxColors.A(FxColors.White, 0.05f), 0.08f, 0.02f, 0.25f, FxColors.A(FxColors.White, 0.6f));
                    }
                    break;
                }
                case "jetpack":           // Python: orange burst 12 + ring; plus a smoke ring on take-off
                    pool.Sparks(ground + Up(0.4f), FxColors.Orange, 12, 12f, 0.4f, 0.12f, Vector3.down, 1.2f);
                    GroundRing(ground, FxColors.Orange, 0.6f, 2.6f, 0.4f);
                    for (int i = 0; i < 8; i++)
                    {
                        float a = i / 8f * Mathf.PI * 2f;
                        pool.Smoke(ground + new Vector3(Mathf.Cos(a), 0.2f, Mathf.Sin(a)) * 1.2f, FxColors.A(FxColors.Smoke, 0.5f), 1, 1.5f, 0.9f, 0.9f, 0.1f);
                    }
                    break;
                case "stomp":             // Python: energy ring over the radius + shake: shockwave, dust, a cracked patch
                    GroundRing(ground, FxColors.Energy, radius * 0.2f, radius, 0.4f, 1.5f);
                    GroundRing(ground, FxColors.White, radius * 0.1f, radius * 0.75f, 0.35f, 1.0f).delay = 0.06f;
                    pool.Chunks(ground + Up(0.2f), FxColors.Dirt, 16, 7f, 0.7f, 0.22f);
                    pool.Smoke(ground + Up(0.3f), FxColors.Dust, 8, 2.5f, 1.2f, 1.4f, radius * 0.5f);
                    var crack = pool.Flat(FxTex.Disc, FxBlend.Alpha, ground + Up(0.05f), FxColors.A(new Color(0.12f, 0.1f, 0.08f), 0.5f), radius * 0.9f, radius * 1.1f, 4f);
                    crack.fadeOut = 0.5f;
                    break;
                case "camo":              // Python: green ring; the cloak shimmers in
                    GroundRing(ground, FxColors.Green, body * 1.5f, body * 0.8f, 0.4f);
                    for (int i = 0; i < 12; i++)
                    {
                        var p = ground + Up(Random.Range(0.3f, 1.6f)) + RandFlat(body * 1.4f);
                        var m = pool.Glow(p, FxColors.Hot(new Color(0.6f, 1f, 0.85f), 1.0f), 0.25f, 0.05f, 0.5f);
                        m.vel = (ground + Up(0.9f) - p) * 1.6f;
                    }
                    break;
                case "stim_field":        // Medics: red ring over the radius, motes on the area
                    GroundRing(ground, FxColors.Red, radius * 0.3f, radius, 0.5f);
                    pool.Motes(ground + Up(0.5f), FxColors.Hot(FxColors.Red, 1.2f), 16, 1.6f, 0.8f, 0.22f, Vector3.up, 0.6f, radius * 0.7f);
                    break;
                case "burrow":            // Python: dirt burst 16; a hole where it went down and a marker where it comes up
                    pool.Chunks(ground + Up(0.2f), FxColors.Dirt, 16, 6f, 0.6f, 0.22f);
                    pool.Smoke(ground + Up(0.3f), FxColors.Dust, 6, 1.5f, 1f, 1.2f, 0.6f);
                    var hole = pool.Flat(FxTex.Disc, FxBlend.Alpha, ground + Up(0.05f), FxColors.A(new Color(0.18f, 0.12f, 0.07f), 0.75f), body * 1.6f, body * 1.8f, 3f);
                    hole.fadeOut = 0.5f;
                    GroundRing(to, FxColors.Dirt, body * 1.6f, body * 1.2f, 1.2f, 1.0f).fadeIn = 0.3f;
                    break;
                case "rally":             // Commander: golden ring over the radius, motes rising, a glint overhead
                    GroundRing(ground, FxColors.Energy, radius * 0.2f, radius, 0.6f, 1.4f);
                    pool.Motes(ground + Up(0.4f), FxColors.Hot(FxColors.Energy, 1.3f), 18, 2.2f, 1.0f, 0.22f, Vector3.up, 0.5f, radius * 0.6f);
                    var glint = pool.Quad(FxFacing.Billboard, FxTex.Star, FxBlend.Add, from + Up(1.5f), FxColors.Hot(FxColors.Energy, 1.6f), 2.5f, 1f, 0.5f);
                    glint.spin = 90f;
                    break;
                case "brood":             // Carrier / Queen / Architect hatch brood: goo
                    weapons.GooBurst(ground + Up(0.5f), 1.4f, true);
                    break;
                default:
                    GroundRing(ground, FxColors.Cyan, body * 0.5f, body * 1.5f, 0.4f);
                    break;
            }
        }

        /// <summary>Ground rings the Sim reports without naming an ability (IBattleFx.Ring). r in world units.
        /// kind: blast / energy / stim / goo / burrow / upgrade, plus stasis / hack from Chrono and Hacker towers.</summary>
        public void Ring(Vector3 ground, float r, string kind)
        {
            switch (kind)
            {
                case "blast":             // dash landings (ram, charge, jetpack): orange ring + dust
                    GroundRing(ground, FxColors.Orange, r * 0.3f, r, 0.4f);
                    pool.Smoke(ground + Up(0.3f), FxColors.Dust, 5, 2f, 0.9f, 1.1f, r * 0.3f);
                    pool.Chunks(ground, FxColors.Dirt, 6, 4.5f, 0.5f, 0.18f);
                    break;
                case "burrow":            // coming up out of the ground
                    pool.Chunks(ground + Up(0.2f), FxColors.Dirt, 18, 7f, 0.7f, 0.24f);
                    pool.Smoke(ground + Up(0.3f), FxColors.Dust, 8, 2f, 1.2f, 1.3f, r * 0.4f);
                    GroundRing(ground, FxColors.Dirt, r * 0.3f, r, 0.5f, 1.0f);
                    break;
                case "goo":
                    weapons.GooBurst(ground + Up(0.5f), Mathf.Max(1f, r * 0.4f), true);
                    break;
                case "stim":
                    GroundRing(ground, FxColors.Red, r * 0.4f, r, 0.45f);
                    break;
                case "upgrade":           // a building or tower finished an upgrade: gold ring and a column of light
                    GroundRing(ground, FxColors.Energy, r * 0.5f, r * 1.1f, 0.7f, 1.5f);
                    pool.Line(ground, ground + Up(8f), FxColors.A(FxColors.Hot(FxColors.Energy, 1.3f), 0.8f), r * 0.5f, r * 0.1f, 0.6f, FxColors.A(FxColors.Energy, 0f));
                    pool.Motes(ground + Up(0.5f), FxColors.Hot(FxColors.Energy, 1.4f), 16, 4f, 1.0f, 0.25f, Vector3.up, 0.4f, r * 0.6f);
                    break;
                case "stasis":            // Chrono tower freezes a unit (Python: energy ring 22 px, 0.4 s)
                    GroundRing(ground, FxColors.Energy, r * 1.4f, r, 0.4f, 1.4f);
                    pool.Quad(FxFacing.Billboard, FxTex.Star, FxBlend.Add, ground + Up(1.2f), FxColors.Hot(FxColors.Energy, 1.3f), r * 1.6f, r * 0.8f, 0.4f).spin = 120f;
                    break;
                case "hack":              // Hacker tower takes a unit over for a moment (Python: magenta ring)
                    GroundRing(ground, FxColors.Magenta, r * 1.4f, r, 0.4f, 1.4f);
                    pool.Motes(ground + Up(1f), FxColors.Hot(FxColors.Magenta, 1.3f), 6, 2f, 0.4f, 0.18f);
                    break;
                case "scan":
                    GroundRing(ground, FxColors.Green, r * 0.3f, r, 0.8f);
                    break;
                default:                  // energy: stomp from a unit without an Ability event, bunker loads, deaths of timed units
                    GroundRing(ground, FxColors.Energy, r * 0.3f, r, 0.4f, 1.3f);
                    break;
            }
        }

        /// <summary>Sensor Array Scan: a green circle that stays for the scan's time with radar sweeps
        /// (Python: green ring 0.8 s + the outline while it lasts). r in world units.</summary>
        public void Scan(Vector3 ground, float r, float life)
        {
            var g = ground + Up(0.18f);
            var edge = pool.Flat(FxTex.Ring, FxBlend.Add, g, FxColors.Hot(FxColors.Green, 1.2f), RingSize(r) * 0.3f, RingSize(r), life);
            edge.ease = 8f;
            edge.fadeOut = 0.12f;
            var fill = pool.Flat(FxTex.Glow, FxBlend.Add, g, FxColors.A(FxColors.Green, 0.18f), r * 2.4f, r * 2.4f, life);
            fill.fadeIn = 0.05f;
            fill.fadeOut = 0.12f;
            pool.Quad(FxFacing.Billboard, FxTex.Star, FxBlend.Add, g + Up(2f), FxColors.Hot(FxColors.Green, 1.4f), r * 0.6f, r * 0.3f, 0.6f).spin = 60f;
            pool.Repeat(life, 1.5f, age =>
            {
                var s = pool.Flat(FxTex.Ring, FxBlend.Add, g, FxColors.Hot(FxColors.Green, 0.9f), RingSize(r) * 0.05f, RingSize(r), 1.2f);
                s.ease = 1.3f;
            });
        }

        /// <summary>Hook for leader and alien powers (roadmap step 6). Until each power gets its own look this draws a
        /// generic strike from the sky: a beam, an explosion and a white ring (Python orbital strike). r in world units.</summary>
        public void Power(string id, string faction, Vector3 ground, float r)
        {
            var c = faction == "player" ? FxColors.Cyan : FxColors.AlienGreen;
            pool.Line(ground + Up(40f), ground, FxColors.A(FxColors.Hot(c, 1.3f), 0.1f), Mathf.Max(1f, r * 0.4f), 0.4f, 0.5f, FxColors.Hot(c, 1.6f));
            weapons.Explosion(ground, c, Mathf.Max(1.5f, r * 0.6f), true, false);
            GroundRing(ground, FxColors.White, r * 0.2f, r, 0.5f, 1.4f);
        }
    }
}
