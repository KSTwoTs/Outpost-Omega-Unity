using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;
using Outpost.Content;

namespace Outpost.Sim
{
    /// <summary>
    /// Towers: the per-frame update for every behavior group (see TowerBehaviors), tiers with upgrade and sell,
    /// tower buffs (overclock / generator / mirror / radar), harvester income, Bio-Lab bounties, Uplinks and
    /// taunting towers. Port of sim.py update_tower, tower_fire, tower_target, tower_damage, update_support
    /// (tower part), upgrade_tower and sell_tower. Bunkers are in Battle.Bunkers.
    /// </summary>
    public partial class Battle
    {
        public const float SellRefund = 0.6f;

        // ------------------------------------------------------------------ upgrade / sell
        /// <summary>Raises a finished tower one tier (towers.json tiers[n].upgrade_cost x cost_mult). Error or null.</summary>
        public string UpgradeTower(Tower t)
        {
            if (t == null || !t.alive) return "Tower lost";
            int cost = t.UpgradeCost();
            if (cost < 0) return "Max tier";
            if (!t.Done) return "Still building";
            if (!Pay(t.faction, cost)) return "Not enough " + factions[t.faction].ResName;
            t.tier++;
            t.ApplyTier();
            t.costPaid += cost;
            fx?.Ring(t.Pos, 30, "upgrade");
            return null;
        }

        /// <summary>What selling gives back: 60% of everything paid for the tower and its upgrades.</summary>
        public int SellValue(Tower t) => (int)(t.costPaid * SellRefund);

        public string SellTower(Tower t)
        {
            if (t == null || !t.alive) return "Tower lost";
            factions[t.faction].res += SellValue(t);
            t.alive = false;
            TowerGone(t, false);
            fx?.Died(t);
            return null;
        }

        /// <summary>A tower is gone: frees its socket and ground; a Bunker lets its squads out (hurt by half if destroyed).</summary>
        void TowerGone(Tower t, bool destroyed)
        {
            if (t.garrison.Count > 0) Unload(t, destroyed ? 0.5f : 0f);
            if (t.baseRef != null && t.socket >= 0 && t.baseRef.sockets[t.socket] == t) t.baseRef.sockets[t.socket] = null;
            if (t.baseRef != null) grid.BlockCircle(t.x, t.y, 14, -1);
        }

        // ------------------------------------------------------------------ numbers
        /// <summary>Tower damage: towers.json number x damage_mult x buffs, and the player's weapon research.</summary>
        public float TowerDamage(Tower t, float amount)
        {
            float d = amount * t.scaleDamage * t.buffDamage;
            if (t.faction == "player")
            {
                var r = factions["player"].research;
                if (r.Contains("weapons1")) d *= 1.15f;
                if (r.Contains("weapons2")) d *= 1.15f;
            }
            return d;
        }

        /// <summary>Closest visible enemy outside the minimum range, units before structures (sim.py tower_target).</summary>
        Entity TowerTarget(Tower t, float rng, bool air)
        {
            Entity best = null;
            float bd = float.MaxValue, minr2 = t.minRange * t.minRange;
            foreach (var e in EnemiesNear(t.faction, t.x, t.y, rng, air, true))
            {
                if (HiddenFrom(e, t.faction)) continue;
                float d = D2(t.x, t.y, e.x, e.y);
                if (d < minr2) continue;
                float score = d + (e.IsUnit ? 0 : 90000);
                if (score < bd) { bd = score; best = e; }
            }
            return best;
        }

        /// <summary>Overclock (+fire rate), Power Generator (+damage), Mirror (+range), Radar (+damage) on towers nearby; every 0.5 s.</summary>
        void UpdateTowerBuffs()
        {
            foreach (var t in towers) t.buffRate = t.buffDamage = t.buffRange = 1f;
            foreach (var s in towers)
            {
                if (!s.alive || !s.Done || s.group != TowerBehaviors.Group.Buff) continue;
                float reach = (s.range + 20) * (s.range + 20);
                foreach (var t in towers)
                {
                    if (t == s || !t.alive || t.faction != s.faction || D2(s.x, s.y, t.x, t.y) > reach) continue;
                    switch (s.behavior)
                    {
                        case "overclock": t.buffRate *= 1 + GameData.F(s.stats, "haste", 0.5f) * 0.6f; break;
                        case "generator": t.buffDamage *= 1 + GameData.F(s.stats, "power", 0.2f); break;
                        case "mirror": t.buffRange *= 1.15f; break;
                        case "support": t.buffDamage *= 1 + GameData.F(s.stats, "buff", 0.15f); break;
                    }
                }
            }
        }

        /// <summary>Harvester income per second for a human side (towers.json income / 20).</summary>
        float TowerIncome(string faction)
        {
            if (faction == "alien") return 0f;
            float inc = 0f;
            foreach (var t in towers)
                if (t.alive && t.Done && t.faction == faction && t.behavior == "income") inc += GameData.F(t.stats, "income") / 20f;
            return inc;
        }

        /// <summary>Bio-Labs pay Supplies for every enemy unit killed in their range.</summary>
        void TowerBounty(Entity dead)
        {
            foreach (var t in towers)
            {
                if (!t.alive || !t.Done || t.behavior != "biolab" || !Hostile(t.faction, dead.faction)) continue;
                if (D2(t.x, t.y, dead.x, dead.y) > t.range * t.range) continue;
                factions[t.faction].res += GameData.F(t.stats, "per_kill", 2) * 8;
            }
        }

        /// <summary>Uplinks: each finished one cuts leader power costs by 10%, at most 30%.</summary>
        float TowerPowerCut(string faction) =>
            Mathf.Min(0.3f, 0.1f * towers.Count(t => t.alive && t.Done && t.faction == faction && t.behavior == "uplink"));

        /// <summary>
        /// A unit's target pick with taunting: Barricades, Bait and Decoys in reach draw fire whenever nothing
        /// better than a structure is around, and 60% of the time even then (sim.py acquire).
        /// </summary>
        Entity AcquireFor(Unit u, float radius)
        {
            if (u.damage <= 0) return null;
            var best = Acquire(u.faction, u.x, u.y, radius, u.CanHit);
            Tower taunt = null;
            foreach (var e in Near(u.x, u.y, radius + 40))
            {
                if (!(e is Tower tw) || !tw.alive || !tw.Done || tw.group != TowerBehaviors.Group.Taunt) continue;
                if (!Hostile(tw.faction, u.faction) || !u.CanHit(tw)) continue;
                if (D2(u.x, u.y, tw.x, tw.y) > (radius + tw.radius) * (radius + tw.radius)) continue;
                taunt = tw;
            }
            if (taunt != null && (best == null || !best.IsUnit || Random.value < 0.6f)) return taunt;
            return best;
        }

        // ------------------------------------------------------------------ update
        void UpdateTower(Tower t, float dt)
        {
            t.TickStatus(dt);
            t.spin += dt;
            if (!t.Done)
            {
                t.buildLeft -= dt;
                t.hp = Mathf.Min(t.maxHp, t.hp + t.maxHp * 0.9f * dt / t.buildTotal);
                return;
            }
            if (t.IsBunker)
            {
                t.cooldown -= dt;
                BunkerFire(t, dt);
                return;
            }
            if (t.stunT > 0) return;
            var st = t.stats;
            float rng = t.range * t.buffRange;
            t.cooldown -= dt * t.buffRate;
            switch (t.group)
            {
                case TowerBehaviors.Group.Attack:
                {
                    t.retarget -= dt;
                    var cur = t.target;
                    if (cur == null || !cur.alive || t.retarget <= 0 || HiddenFrom(cur, t.faction) ||
                        D2(t.x, t.y, cur.x, cur.y) > (rng + cur.radius) * (rng + cur.radius))
                    {
                        t.retarget = 0.3f;
                        var nt = TowerTarget(t, rng, t.hitsAir);
                        if (nt != t.target) t.beamT = 0f;
                        t.target = nt;
                    }
                    var tg = t.target;
                    if (tg == null) return;
                    t.angle = Mathf.Atan2(tg.y - t.y, tg.x - t.x);
                    if (t.cooldown > 0)
                    {
                        if (t.behavior == "beam") t.beamT += dt;
                        return;
                    }
                    t.cooldown = 1f / Mathf.Max(0.03f, t.rate);
                    TowerFire(t, tg, rng);
                    break;
                }
                case TowerBehaviors.Group.Slow:
                {
                    if (t.cooldown > 0) return;
                    t.cooldown = Mathf.Max(0.05f, t.cooldown + 0.25f);    // a steady 4 pulses a second whatever the frame step
                    float slow = TowerBehaviors.SlowAmount(t.behavior, st), dps = TowerBehaviors.SlowDps(st);
                    var targets = EnemiesNear(t.faction, t.x, t.y, rng, t.hitsAir, true);
                    foreach (var e in targets)
                    {
                        if (slow > 0) e.ApplySlow(slow, 0.5f);
                        if (dps > 0) Hit(e, TowerDamage(t, dps * 0.25f), "bld", t, t.faction, t.behavior == "magnet");
                    }
                    if (t.behavior == "fence")
                        for (int i = 0; i < Mathf.Min(3, targets.Count); i++) fx?.Shot(t, t.Pos, targets[i].Pos, t.faction, 1);
                    break;
                }
                case TowerBehaviors.Group.Shockwave:
                {
                    if (t.cooldown > 0) return;
                    var targets = EnemiesNear(t.faction, t.x, t.y, rng, false, true);
                    if (targets.Count == 0) return;
                    t.cooldown = 1f / Mathf.Max(0.03f, GameData.F(st, "fire_rate", 0.5f));
                    float stun = GameData.F(st, "stun", 0.3f) * 2.5f;
                    foreach (var e in targets)
                    {
                        Hit(e, TowerDamage(t, GameData.F(st, "damage", 20)), "bld", t, t.faction);
                        e.stunT = Mathf.Max(e.stunT, stun);
                    }
                    fx?.Ring(t.Pos, rng, "energy");
                    break;
                }
                case TowerBehaviors.Group.Disable:
                {
                    if (t.cooldown > 0) return;
                    var targets = EnemiesNear(t.faction, t.x, t.y, rng, t.hitsAir, true).Where(e => e.IsUnit).ToList();
                    if (targets.Count == 0) return;
                    t.cooldown = 1f / Mathf.Max(0.03f, GameData.F(st, "fire_rate", 0.25f));
                    targets.Sort((a, b) => b.hp.CompareTo(a.hp));
                    float dur = t.behavior == "chrono" ? GameData.F(st, "rewind") : GameData.F(st, "hack_time", 3f);
                    foreach (var e in targets.Take(Mathf.Max(1, GameData.I(st, "targets", 1))))
                    {
                        e.stunT = Mathf.Max(e.stunT, dur);
                        fx?.Shot(t, t.Pos, e.Pos, t.faction, 1);
                        fx?.Ring(e.Pos, 22, "energy");
                    }
                    break;
                }
                case TowerBehaviors.Group.Spawn:
                {
                    if (t.cooldown > 0) return;
                    if (EnemiesNear(t.faction, t.x, t.y, rng, false, true).Count == 0) return;
                    t.cooldown = 1f / Mathf.Max(0.03f, GameData.F(st, "fire_rate", 0.25f));
                    for (int i = 0; i < (int)GameData.F(st, "hatch", 2); i++)
                        SpawnBroodling(t, GameData.F(st, "life", 8f), GameData.F(st, "contact_dps", 25) / 4f);
                    break;
                }
                case TowerBehaviors.Group.Repair:
                {
                    float heal = GameData.F(st, "heal", 6) * 3 * dt;
                    foreach (var e in Near(t.x, t.y, rng + 30))
                        if (e.alive && e != t && e.faction == t.faction && e.hp < e.maxHp &&
                            D2(t.x, t.y, e.x, e.y) <= (rng + e.radius) * (rng + e.radius))
                            e.hp = Mathf.Min(e.maxHp, e.hp + heal);
                    break;
                }
                case TowerBehaviors.Group.Shield:
                {
                    float cap = GameData.F(st, "shield", 40) * 5, regen = GameData.F(st, "regen", 6) * 3 * dt, delay = GameData.F(st, "delay", 3f);
                    foreach (var e in Near(t.x, t.y, rng + 30))
                        if (e.alive && e.faction == t.faction && e.lastHitT > delay &&
                            D2(t.x, t.y, e.x, e.y) <= (rng + e.radius) * (rng + e.radius))
                            e.shield = Mathf.Min(cap, e.shield + regen);
                    break;
                }
                case TowerBehaviors.Group.Taunt:
                    if (t.behavior == "bait") t.hp = Mathf.Min(t.maxHp, t.hp + GameData.F(st, "regen", 3) * 4 * dt);
                    break;
                // Buff (UpdateTowerBuffs), Income (Economy), Bounty (Kill) and Uplink (Recount) have nothing to do per frame
            }
        }

        void TowerFire(Tower t, Entity tg, float rng)
        {
            var st = t.stats;
            float dmg = TowerDamage(t, TowerBehaviors.ShotDamage(t.behavior, st));
            switch (t.behavior)
            {
                case "projectile":
                    projectiles.Add(NewProjectile(t.kind == "spine" ? "acid" : "bolt", t, tg, dmg, 0, "bld", t.hitsAir, 560));
                    break;
                case "drones":
                    fx?.Shot(t, t.Pos, tg.Pos, t.faction, Mathf.Max(1, GameData.I(st, "drones", 2)));
                    Hit(tg, dmg, "bld", t, t.faction);
                    break;
                case "pierce":
                {
                    var dir = new Vector2(Mathf.Cos(t.angle), Mathf.Sin(t.angle));
                    var end = t.Pos + dir * rng;
                    fx?.Beam(t.Pos, end);
                    foreach (var e in EnemiesNear(t.faction, (t.x + end.x) / 2, (t.y + end.y) / 2, rng / 2 + 10, false, true))
                    {
                        var rel = e.Pos - t.Pos;
                        float along = Vector2.Dot(rel, dir), perp = Mathf.Abs(rel.x * dir.y - rel.y * dir.x);
                        if (along >= 0 && along <= rng && perp <= e.radius + 6) Hit(e, dmg, "bld", t, t.faction, true);
                    }
                    break;
                }
                case "missile":
                case "flak":
                {
                    bool missile = t.behavior == "missile";
                    for (int i = 0; i < Mathf.Max(1, GameData.I(st, "burst", 1)); i++)
                    {
                        var p = NewProjectile(missile ? "rocket" : "flak", t, tg, dmg, GameData.F(st, "splash", 40), "bld", t.hitsAir, missile ? 380 : 620);
                        p.delay = i * 0.12f;
                        projectiles.Add(p);
                    }
                    break;
                }
                case "beam":
                {
                    float ramp = GameData.F(st, "ramp", 2f);
                    float k = 1 + (ramp - 1) * Mathf.Min(1f, t.beamT / Mathf.Max(0.1f, GameData.F(st, "ramp_time", 3f)));
                    t.beamT += 0.2f;
                    Hit(tg, dmg * k, "bld", t, t.faction);
                    fx?.Beam(t.Pos, tg.Pos);
                    break;
                }
                case "chain":
                case "lightning":
                {
                    int n = Mathf.Max(1, GameData.I(st, "chain", 3));
                    var pts = new List<Vector2> { t.Pos };
                    var hitSet = new HashSet<Entity>();
                    var cur = tg;
                    for (int i = 0; i < n && cur != null; i++)
                    {
                        hitSet.Add(cur);
                        pts.Add(cur.Pos);
                        Hit(cur, dmg * Mathf.Pow(0.85f, i), "bld", t, t.faction);
                        Entity next = null;
                        float bd = float.MaxValue;
                        foreach (var e in EnemiesNear(t.faction, cur.x, cur.y, t.chainRange * 1.2f, t.hitsAir, true))
                        {
                            if (!e.alive || hitSet.Contains(e) || HiddenFrom(e, t.faction)) continue;
                            float d = D2(cur.x, cur.y, e.x, e.y);
                            if (d < bd) { bd = d; next = e; }
                        }
                        cur = next;
                    }
                    fx?.Lightning(pts, tg.flying);
                    break;
                }
                case "cone":
                {
                    float cone = GameData.F(st, "cone", 0.45f) * 1.3f;
                    foreach (var e in EnemiesNear(t.faction, t.x, t.y, rng, false, true))
                    {
                        float a = Mathf.Atan2(e.y - t.y, e.x - t.x);
                        if (Mathf.Abs(Mathf.DeltaAngle(a * Mathf.Rad2Deg, t.angle * Mathf.Rad2Deg)) * Mathf.Deg2Rad <= cone)
                            Hit(e, dmg, "bld", t, t.faction, false, ZoneBonus);
                    }
                    fx?.Shot(t, t.Pos, t.Pos + new Vector2(Mathf.Cos(t.angle), Mathf.Sin(t.angle)) * rng, t.faction, 2);
                    break;
                }
                case "mortar":
                case "mines":
                    Lob(t, tg.Pos, dmg, GameData.F(st, "splash", 50), t.behavior == "mortar" ? 1f : 0.4f);
                    break;
                case "ion":
                    fx?.Beam(t.Pos, tg.Pos);
                    Splash(tg.Pos, 50, dmg, "bld", t, t.faction, true);
                    fx?.Impact(tg.Pos, t.faction, 30, tg.flying);
                    break;
                case "acid":
                {
                    float r = GameData.F(st, "radius", 34) * 1.3f, life = GameData.F(st, "duration", 4f);
                    zones.Add(new Zone { pos = tg.Pos, r = r, dps = dmg, t = life, faction = t.faction, kind = "acid" });
                    fx?.Zone(tg.Pos, r, life, "acid");
                    fx?.Shot(t, t.Pos, tg.Pos, t.faction, 1);
                    break;
                }
            }
        }

        /// <summary>A Brood Turret hatchling: a fast short-lived biter (sim.py BROODLING), shown with the Skitter model.</summary>
        Unit SpawnBroodling(Tower t, float life, float damage)
        {
            var j = Jitter(20);
            var u = SpawnUnit("skitters", t.faction, t.x + j.x, t.y + j.y);
            u.name = "Broodling";
            u.cls = "inf";
            u.flying = false;
            u.squad = 1;
            u.memberHp = u.maxHp = u.hp = 30;
            u.armor = 0;
            u.speed = 115;
            u.range = 24;
            u.minRange = 0;
            u.damage = damage;
            u.rate = 2;
            u.splash = 0;
            u.hitsAir = false;
            u.hitsGround = true;
            u.pop = 0;
            u.sight = 180;
            u.lifetime = life;
            u.ability = null;
            u.bonus = null;
            return u;
        }
    }
}
