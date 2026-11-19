using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;
using Outpost.Content;

namespace Outpost.Sim
{
    /// <summary>
    /// Unit abilities and the buffs they give (frag, salvo, napalm, stim, ram, jetpack, burrow, brood...).
    /// </summary>
    public partial class Battle
    {
        // ------------------------------------------------------------------ abilities
        public bool AbilityReady(Unit u)
        {
            if (u.ability == null || u.abilityCd > 0) return false;
            string need = GameData.S(u.ability, "needs");
            return need == null || factions[u.faction].research.Contains(need);
        }

        /// <summary>Does this ability need a point on the ground (aimed), or does it just go off?</summary>
        public static bool AbilityAimed(Unit u) => u.ability != null && GameData.F(u.ability, "range") > 0;

        /// <summary>A unit's special ability. Aimed abilities take a point; others use the target or the unit.</summary>
        public bool UseAbility(Unit u, Vector2? at = null)
        {
            if (!u.alive || !AbilityReady(u)) return false;
            var ab = u.ability;
            string id = GameData.S(ab, "id", "");
            var p = at ?? (u.target != null && u.target.alive ? u.target.Pos : u.Pos);
            float rng = GameData.F(ab, "range");
            var toP = p - u.Pos;
            if (rng > 0 && toP.magnitude > rng) p = u.Pos + toP.normalized * rng;   // clamp to max range
            float dm = UnitMods(u.faction, u.kind).dmg;
            float dmg = GameData.F(ab, "damage"), radius = GameData.F(ab, "radius"), dur = GameData.F(ab, "duration");
            fx?.Ability(u, id, p);
            switch (id)
            {
                case "frag":
                case "canister":
                    Lob(u, p, dmg * dm, radius, 0.7f);
                    break;
                case "salvo":
                    for (int i = 0; i < 4; i++)
                        strikes.Add(new Strike { t = 0.25f + i * 0.15f, pos = p + Jitter(25), r = radius, dmg = dmg / 2 * dm,
                            faction = u.faction, cls = "air", from = u.Pos, air = true });
                    break;
                case "barrage":
                    for (int i = 0; i < 6; i++)
                        strikes.Add(new Strike { t = 0.4f + i * 0.2f, pos = p + Jitter(60), r = 55, dmg = dmg / 3 * dm,
                            faction = u.faction, cls = "air", from = u.Pos, air = false });
                    break;
                case "napalm":
                    zones.Add(new Zone { pos = p, r = radius, dps = dmg * dm, t = dur, faction = u.faction, kind = "fire" });
                    fx?.Zone(p, radius, dur, "fire");
                    fx?.Trail(u.Pos, p, u.faction);
                    break;
                case "stim":
                case "frenzy":
                case "lockdown":
                    u.buffs[id] = dur;
                    if (id == "lockdown")
                    {
                        u.path.Clear();
                        u.order = Order.Hold;
                    }
                    fx?.Ring(u.Pos, u.radius + 14, "stim");
                    break;
                case "ram":
                case "charge":
                    u.dash = new Dash { to = p, speed = u.speed * 3.2f, radius = radius, damage = dmg };
                    u.path.Clear();
                    break;
                case "jetpack":
                    u.dash = new Dash { to = p, speed = 520, radius = radius, damage = dmg, fly = true };
                    u.path.Clear();
                    fx?.Ring(u.Pos, 24, "blast");
                    break;
                case "stomp":
                    foreach (var e in EnemiesNear(u.faction, u.x, u.y, radius, false, true))
                    {
                        Hit(e, dmg * dm, u.cls, u, u.faction);
                        e.stunT = Mathf.Max(e.stunT, GameData.F(ab, "stun", 1f));
                    }
                    fx?.Ring(u.Pos, radius, "energy");
                    break;
                case "camo":
                    u.buffs["camo"] = dur;
                    fx?.Ring(u.Pos, 24, "goo");
                    break;
                case "stim_field":
                    foreach (var e in Near(u.x, u.y, radius).ToArray())
                        if (e is Unit o && o.alive && o.faction == u.faction && (o.cls == "inf" || o.cls == "hero") &&
                            D2(u.x, u.y, o.x, o.y) <= (radius + o.radius) * (radius + o.radius))
                            o.buffs["stim"] = dur;
                    fx?.Ring(u.Pos, radius, "stim");
                    break;
                case "burrow":
                    u.burrowTo = grid.NearestWalkablePx(grid.Clamp(p), 6);
                    u.buffs["burrowed"] = 0.8f + (p - u.Pos).magnitude / 380f;
                    u.path.Clear();
                    u.order = Order.None;
                    u.target = null;
                    fx?.Ring(u.Pos, 30, "burrow");
                    break;
                case "rally":
                    foreach (var e in Near(u.x, u.y, radius).ToArray())
                        if (e is Unit o && o.alive && o.faction == u.faction &&
                            D2(u.x, u.y, o.x, o.y) <= (radius + o.radius) * (radius + o.radius))
                            o.buffs["rally"] = dur;
                    fx?.Ring(u.Pos, radius, "energy");
                    break;
                case "brood":
                {
                    string spawn = GameData.S(ab, "spawn", "skitters");
                    for (int i = 0; i < (spawn == "skitters" ? 2 : 1); i++)
                    {
                        var j = Jitter(30);
                        var s = SpawnUnit(spawn, u.faction, u.x + j.x, u.y + j.y);
                        if (u.target != null && u.target.alive) OrderAttack(new[] { s }, u.target);
                    }
                    fx?.Ring(u.Pos, 34, "goo");
                    break;
                }
                default:
                    return false;
            }
            u.abilityCd = GameData.F(ab, "cooldown", 10);
            if (id != "camo") u.buffs.Remove("camo");
            return true;
        }

        static Vector2 Jitter(float r) => new Vector2(Random.Range(-r, r), Random.Range(-r, r));

        void Lob(Entity src, Vector2 to, float dmg, float radius, float flight)
        {
            var p = new Projectile
            {
                kind = "lob", pos = src.Pos, dest = to, dmg = dmg, splash = radius, cls = src.cls, faction = src.faction,
                src = src, hitsAir = false, lob = true, total = flight, bonus = (src as Unit)?.bonus,
            };
            fx?.ProjectileSpawned(p);
            projectiles.Add(p);
        }
    }
}
