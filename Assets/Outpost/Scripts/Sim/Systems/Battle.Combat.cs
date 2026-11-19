using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;
using Outpost.Content;

namespace Outpost.Sim
{
    /// <summary>
    /// Combat: damage (counters, bonus tables, armor, shields), splash, kills and veterancy, projectiles, strikes and zones.
    /// </summary>
    public partial class Battle
    {
        // ------------------------------------------------------------------ damage
        /// <summary>Hostile entities touching the circle (a fresh list, safe to damage while looping).</summary>
        public List<Entity> EnemiesNear(string faction, float x, float y, float r, bool air = true, bool ground = true)
        {
            var res = new List<Entity>();
            foreach (var e in Near(x, y, r + 40))
            {
                if (!e.alive || !Hostile(e.faction, faction)) continue;
                if (e.flying ? !air : !ground) continue;
                if (D2(x, y, e.x, e.y) <= (r + e.radius) * (r + e.radius)) res.Add(e);
            }
            return res;
        }

        /// <summary>
        /// Applies damage with class counters, the attacker's bonus table and armor (armor never cuts
        /// more than 75%). Burrowed units can't be hurt.
        /// </summary>
        public float Hit(Entity t, float amount, string cls, Entity src, string faction, bool pierce = false, JObject bonus = null)
        {
            if (t == null || !t.alive || amount <= 0) return 0;
            if (t is Unit tu && tu.Has("burrowed")) return 0;
            float amt = amount * GameData.Counter(cls, t.cls) * (bonus != null ? GameData.F(bonus, t.cls, 1f) : 1f);
            if (!pierce) amt = Mathf.Max(amt * 0.25f, amt - t.armor);
            if (t.shield > 0)
            {
                float soak = Mathf.Min(t.shield, amt);
                t.shield -= soak;
                amt -= soak;
            }
            t.hp -= amt;
            t.lastHitT = 0;
            if (t.hp <= 0) Kill(t, src);
            return amt;
        }

        /// <summary>Area damage: full at the centre, falling to half at the edge.</summary>
        public void Splash(Vector2 at, float r, float amount, string cls, Entity src, string faction, bool hitsAir, JObject bonus = null)
        {
            foreach (var e in EnemiesNear(faction, at.x, at.y, r, hitsAir, true))
            {
                float d = Mathf.Sqrt(D2(at.x, at.y, e.x, e.y));
                float fall = 1f - 0.5f * Mathf.Min(1f, d / Mathf.Max(1f, r));
                Hit(e, amount * fall, cls, src, faction, false, bonus);
            }
        }

        public void Kill(Entity e, Entity src)
        {
            if (!e.alive) return;
            e.alive = false;
            e.hp = 0;
            if (src is Unit k && k.alive)
            {
                k.kills++;
                var vet = GameData.Rts["veterancy"];
                var thr = vet?["kills"];
                int rank = thr != null ? thr.Count(v => k.kills >= (int)v) : 0;
                if (rank > k.rank)
                {
                    // each rank also toughens the squad
                    float hpStep = GameData.F(vet, "hp", 0.1f), ratio = k.Ratio;
                    k.memberHp *= (1 + hpStep * rank) / (1 + hpStep * k.rank);
                    k.maxHp = k.memberHp * k.squad;
                    k.hp = k.maxHp * ratio;
                    k.rank = rank;
                }
            }
            if (src != null && factions.TryGetValue(src.faction, out var kf)) kf.kills++;
            var ef = factions[e.faction];
            ef.losses++;
            if (e == ef.hero)
            {
                ef.hero = null;
                ef.heroRespawn = GameData.F(e.data, "respawn", 30);
                if (e.faction == "player") fx?.Message("alien", $"Commander down! Respawning at HQ in {(int)ef.heroRespawn} s");
            }
            if (e is Building b)
            {
                grid.BlockCircle(b.x, b.y, b.IsHq ? 44 : 26, -1);
                if (b.IsHq)
                {
                    if (b.faction == "player") fx?.Message("alien", "We lost a base!");
                    // the base falls with its core
                    foreach (var s in b.baseRef.slots) if (s != null && s.alive) Kill(s, src);
                    foreach (var s in b.baseRef.sockets) if (s != null && s.alive) Kill(s, src);
                }
                else if (b.baseRef.slots[b.slot] == b) b.baseRef.slots[b.slot] = null;
                foreach (var q in b.queue)
                {
                    factions[b.faction].popUsed -= q.pop;
                    if (q.type == "research") factions[b.faction].researching.Remove(q.id);
                }
                b.queue.Clear();
            }
            if (e is Tower t) TowerGone(t, true);
            if (e is Unit) TowerBounty(e);      // Bio-Labs near the kill pay out
            fx?.Died(e);
        }

        Projectile NewProjectile(string kind, Entity src, Entity tg, float dmg, float splash, string cls, bool air, float speed)
        {
            var p = new Projectile
            {
                kind = kind, pos = src.Pos, target = tg, dest = tg.Pos, speed = speed, dmg = dmg, splash = splash,
                cls = cls, faction = src.faction, src = src, hitsAir = air, bonus = (src as Unit)?.bonus,
            };
            fx?.ProjectileSpawned(p);
            return p;
        }

        void UpdateProjectiles(float dt)
        {
            for (int i = projectiles.Count - 1; i >= 0; i--)
            {
                var p = projectiles[i];
                if (p.delay > 0)
                {
                    p.delay -= dt;      // later shots of a burst wait at the muzzle
                    if (p.delay > 0) continue;
                    p.pos = p.src != null ? p.src.Pos : p.pos;
                }
                if (p.target != null && p.target.alive && !p.lob) p.dest = p.target.Pos;
                bool arrived;
                if (p.lob)
                {
                    p.t += dt;
                    arrived = p.t >= p.total;
                }
                else
                {
                    var d = p.dest - p.pos;
                    float step = p.speed * dt;
                    arrived = d.magnitude <= step + 2;
                    p.pos = arrived ? p.dest : p.pos + d.normalized * step;
                }
                if (!arrived) continue;
                projectiles.RemoveAt(i);
                fx?.ProjectileEnded(p);
                if (p.splash > 0)
                {
                    Splash(p.dest, p.splash, p.dmg, p.cls, p.src, p.faction, p.hitsAir, p.bonus);
                    fx?.Impact(p.dest, p.faction, p.splash, false);
                }
                else
                {
                    if (p.target != null && p.target.alive) Hit(p.target, p.dmg, p.cls, p.src, p.faction, false, p.bonus);
                    fx?.Impact(p.dest, p.faction, 10, p.target != null && p.target.flying);
                }
            }
        }

        void UpdateStrikes(float dt)
        {
            for (int i = strikes.Count - 1; i >= 0; i--)
            {
                var s = strikes[i];
                s.t -= dt;
                if (s.t > 0) continue;
                strikes.RemoveAt(i);
                Splash(s.pos, s.r, s.dmg, s.cls, null, s.faction, s.air);
                fx?.Trail(s.from, s.pos, s.faction);
                fx?.Impact(s.pos, s.faction, s.r, false);
            }
        }

        static readonly JObject ZoneBonus = new JObject { ["inf"] = 1.3f };

        void UpdateZones(float dt)
        {
            for (int i = zones.Count - 1; i >= 0; i--)
            {
                var z = zones[i];
                z.t -= dt;
                if (z.t <= 0) { zones.RemoveAt(i); continue; }
                z.tick -= dt;
                if (z.tick > 0) continue;
                z.tick = 0.25f;
                foreach (var e in EnemiesNear(z.faction, z.pos.x, z.pos.y, z.r, false, true))
                    Hit(e, z.dps * 0.25f, "bld", null, z.faction, z.kind == "acid" || z.kind == "spore", ZoneBonus);
            }
        }
    }
}
