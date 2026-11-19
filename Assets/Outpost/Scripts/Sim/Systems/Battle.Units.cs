using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;
using Outpost.Content;

namespace Outpost.Sim
{
    /// <summary>
    /// The per-unit update: timed units, burrowing, auras, reinforcement, dashes, targeting, firing, movement,
    /// pathing, stuck detection and separation.
    /// </summary>
    public partial class Battle
    {
        // ------------------------------------------------------------------ units
        void UpdateUnit(Unit u, float dt)
        {
            if (u.inBunker != null) return;
            u.TickStatus(dt);
            u.TickBuffs(dt);
            u.moving = false;
            if (u.lifetime >= 0)
            {
                u.lifetime -= dt;
                if (u.lifetime <= 0)
                {
                    // timed units (Mech Drop, drones) just leave
                    u.alive = false;
                    fx?.Ring(u.Pos, 30, "energy");
                    fx?.Died(u);
                    return;
                }
            }
            if (u.burrowTo.HasValue && !u.Has("burrowed"))
            {
                // tunnelling units erupt at the far end
                var bt = u.burrowTo.Value;
                u.burrowTo = null;
                u.x = bt.x;
                u.y = bt.y;
                float er = GameData.F(u.ability, "radius", 60);
                Splash(u.Pos, er, GameData.F(u.ability, "damage", 50), u.cls, u, u.faction, false);
                fx?.Ring(u.Pos, er, "burrow");
                u.guard = u.lastPos = u.Pos;
            }
            if (u.Has("burrowed") || u.stunT > 0) return;
            if (u.cooldown > 0) u.cooldown -= dt;
            if (u.fireT > 0) u.fireT -= dt;
            if (u.heal != null || u.shieldAura != null)
            {
                u.auraT -= dt;
                if (u.auraT <= 0)
                {
                    u.auraT = 0.5f;
                    UnitAuras(u);
                }
            }

            // reinforcement: squads near one of their own base cores refill
            var rf = GameData.Rts["reinforce"];
            if (u.hp < u.maxHp && u.lastHitT > GameData.F(rf, "delay", 4) && u.lifetime < 0)
                foreach (var b in bases)
                    if (b.faction == u.faction && b.Alive && b.hq.Done &&
                        D2(u.x, u.y, b.x, b.y) < Mathf.Pow(GameData.F(rf, "radius", 330), 2))
                    {
                        u.hp = Mathf.Min(u.maxHp, u.hp + u.maxHp * GameData.F(rf, "rate", 0.05f) * dt);
                        break;
                    }

            // dash abilities (Ram, Charge, Jetpack)
            if (u.dash != null)
            {
                var ds = u.dash;
                ds.t += dt;
                bool wasFlying = u.flying;
                if (ds.fly) u.flying = true;
                bool arrived = MoveToward(u, ds.to, dt, 10, ds.speed);
                u.flying = wasFlying;
                if (arrived && ds.fly && !grid.WalkablePx(u.x, u.y, true))
                {
                    var land = grid.NearestWalkablePx(u.Pos, 6);
                    u.x = land.x;
                    u.y = land.y;
                }
                foreach (var e in EnemiesNear(u.faction, u.x, u.y, u.radius + 6, false, true))
                    if (e is Unit && e.cls == "inf" && ds.hit.Add(e)) Hit(e, ds.damage, u.cls, u, u.faction);
                bool blocked = (u.Pos - u.lastPos).sqrMagnitude < 0.01f;
                if (arrived || blocked || ds.t > 2.5f)
                {
                    Splash(u.Pos, ds.radius, ds.damage, u.cls, u, u.faction, false);
                    fx?.Ring(u.Pos, ds.radius, "blast");
                    u.dash = null;
                    u.guard = u.Pos;
                }
                u.lastPos = u.Pos;
                return;
            }

            if (Leashed(u, dt))
            {
                FollowPath(u, dt);
                u.lastPos = u.Pos;
                return;
            }

            // walking into a Bunker (Battle.Bunkers)
            if (u.order == Order.Garrison && UpdateGarrisonOrder(u, dt)) return;

            // targeting
            var t = u.target;
            if (t != null && (!t.alive || !u.CanHit(t) || HiddenFrom(t, u.faction)))
            {
                t = u.target = null;
                if (u.order == Order.Attack) { u.order = Order.None; u.guard = u.Pos; }
            }
            u.retarget -= dt;
            if (u.retarget <= 0)
            {
                u.retarget = 0.3f;
                if (u.order == Order.None || u.order == Order.Hold || (u.order == Order.Move && u.attackMove))
                {
                    float radius = u.order == Order.Hold ? u.AttackRange : Mathf.Max(u.sight, u.AttackRange);
                    var cand = AcquireFor(u, radius);
                    if (cand != null && (t == null || D2(u.x, u.y, cand.x, cand.y) < D2(u.x, u.y, t.x, t.y) * 0.6f))
                        t = u.target = cand;
                }
                else if (u.order == Order.Move && t == null && u.damage > 0)
                {
                    var cand = AcquireFor(u, u.AttackRange);   // shoot while walking
                    if (cand != null) t = u.target = cand;
                }
            }

            bool inRange = false;
            if (t != null)
            {
                float d2 = D2(u.x, u.y, t.x, t.y), reach = u.AttackRange + t.radius;
                inRange = d2 <= reach * reach && d2 >= u.minRange * u.minRange;
                if (inRange)
                {
                    u.angle = Mathf.Atan2(t.y - u.y, t.x - u.x);
                    if (u.cooldown <= 0)
                    {
                        u.cooldown = 1f / Mathf.Max(0.05f, u.FireRate);
                        Fire(u, t);
                    }
                }
                else if (u.order == Order.Move && !u.attackMove && D2(u.x, u.y, t.x, t.y) > Mathf.Pow(u.AttackRange + t.radius + 60, 2))
                    u.target = t = null;   // out of reach while walking: forget it
            }

            // movement
            switch (u.order)
            {
                case Order.Move:
                    if (u.attackMove && t != null)
                    {
                        if (!inRange) Chase(u, t, dt);
                    }
                    else if (FollowPath(u, dt))
                    {
                        u.order = Order.None;
                        u.attackMove = false;
                        u.guard = u.Pos;
                    }
                    break;
                case Order.Attack:
                    if (t != null && !inRange) Chase(u, t, dt);
                    break;
                case Order.None:
                    if (t != null && !inRange)
                    {
                        if (D2(u.x, u.y, u.guard.x, u.guard.y) > Mathf.Pow(u.sight + 220, 2))
                        {
                            u.target = null;
                            OrderMove(new[] { u }, u.guard, false);
                        }
                        else Chase(u, t, dt);
                    }
                    else if (t == null && D2(u.x, u.y, u.guard.x, u.guard.y) > 60 * 60)
                        OrderMove(new[] { u }, u.guard, false);
                    break;
            }

            // stuck detection
            if (u.moving)
            {
                if ((u.Pos - u.lastPos).sqrMagnitude < Mathf.Pow(u.Speed * dt * 0.15f, 2))
                {
                    u.stuckT += dt;
                    if (u.stuckT > 1.5f)
                    {
                        u.stuckT = 0;
                        if (u.path.Count <= 1 && u.goal.HasValue && D2(u.x, u.y, u.goal.Value.x, u.goal.Value.y) < 90 * 90)
                            u.path.Clear();
                        else if (u.path.Count > 0)
                            u.path = grid.Path(u.Pos, u.path[u.path.Count - 1]);
                    }
                }
                else u.stuckT = 0;
            }
            u.lastPos = u.Pos;
            Separate(u, dt);
            // alien units use their abilities on their own
            if (u.faction != "player" && u.ability != null && t != null && t.alive && inRange && AbilityReady(u))
                UseAbility(u, t.Pos);
        }

        void Fire(Unit u, Entity t)
        {
            u.fireT = 0.16f;
            float dmg = u.damage * UnitMods(u.faction, u.kind).dmg * u.Members
                        * (1 + GameData.F(GameData.Rts["veterancy"], "damage", 0.15f) * u.rank);
            if (u.Has("rally")) dmg *= 1.3f;
            u.buffs.Remove("camo");
            if (u.cls == "inf" && t.cls == "inf" && factions[u.faction].research.Contains("hollow_point")) dmg *= 1.2f;
            if (!u.flying && grid.HighGround(u.x, u.y)) dmg *= 1.1f;
            if (u.range <= 50)
            {
                float dealt = Hit(t, dmg, u.cls, u, u.faction, false, u.bonus);
                if (u.lifesteal > 0 && dealt > 0) u.hp = Mathf.Min(u.maxHp, u.hp + dealt * u.lifesteal);
                if (u.splash > 0) Splash(t.Pos, u.splash, dmg * 0.5f, u.cls, u, u.faction, u.hitsAir, u.bonus);
                fx?.Impact(t.Pos, u.faction, 6, t.flying);
                return;
            }
            switch (u.projectile)
            {
                case "bullet":
                    fx?.Shot(u, u.Pos, t.Pos, u.faction, Mathf.Min(4, u.Members));
                    Hit(t, dmg, u.cls, u, u.faction, false, u.bonus);
                    if (u.splash > 0) Splash(t.Pos, u.splash, dmg * 0.4f, u.cls, u, u.faction, u.hitsAir, u.bonus);
                    if (u.burn && t.alive) t.ApplySlow(0.15f, 1f);
                    break;
                case "lob":
                {
                    var p = NewProjectile("lob", u, t, dmg, Mathf.Max(30, u.splash), u.cls, u.hitsAir, 0);
                    p.lob = true;
                    p.dest = t.Pos;
                    p.target = null;
                    p.total = u.range > 200 ? 0.9f : 0.5f;
                    projectiles.Add(p);
                    break;
                }
                default:
                {
                    float speed = u.projectile == "rocket" ? 420 : u.projectile == "shell" ? 520 : u.projectile == "flak" ? 600
                        : u.projectile == "acid" ? 340 : 480;
                    projectiles.Add(NewProjectile(u.projectile, u, t, dmg, u.splash, u.cls, u.hitsAir, speed));
                    break;
                }
            }
        }

        bool MoveToward(Unit u, Vector2 to, float dt, float stop, float speedOverride = -1f)
        {
            var d = to - u.Pos;
            float dist = d.magnitude;
            if (dist <= stop) return true;
            float speed = speedOverride > 0 ? speedOverride : u.Speed;
            if (speed <= 0) return false;
            float step = speed * dt * (u.flying ? 1f : grid.SpeedMult(u.x, u.y));
            step = Mathf.Min(step, dist);
            var n = u.Pos + d / dist * step;
            u.angle = Mathf.Atan2(d.y, d.x);
            Place(u, n);
            u.moving = true;
            return dist - step <= stop;
        }

        void Place(Unit u, Vector2 n)
        {
            if (u.flying)
            {
                n = grid.Clamp(n);
                u.x = n.x;
                u.y = n.y;
                return;
            }
            bool hereOk = grid.WalkablePx(u.x, u.y, true);
            if (grid.WalkablePx(n.x, n.y, true) || !hereOk) { u.x = n.x; u.y = n.y; }
            else if (grid.WalkablePx(n.x, u.y, true)) u.x = n.x;
            else if (grid.WalkablePx(u.x, n.y, true)) u.y = n.y;
            var c = grid.Clamp(u.Pos);
            u.x = c.x;
            u.y = c.y;
        }

        bool FollowPath(Unit u, float dt)
        {
            if (u.path.Count == 0) return true;
            bool last = u.path.Count == 1;
            if (MoveToward(u, u.path[0], dt, last ? 6 : 14)) u.path.RemoveAt(0);
            return u.path.Count == 0;
        }

        void Chase(Unit u, Entity t, float dt)
        {
            if (u.flying || grid.LineClear(u.Pos, t.Pos))
            {
                u.path.Clear();
                MoveToward(u, t.Pos, dt, 0);
                return;
            }
            u.repath -= dt;
            if (u.repath <= 0 || u.path.Count == 0)
            {
                u.path = grid.Path(u.Pos, t.Pos);
                u.repath = 1f + Random.value * 0.5f;
            }
            FollowPath(u, dt);
        }

        void Separate(Unit u, float dt)
        {
            float px = 0, py = 0;
            foreach (var o in Near(u.x, u.y, u.radius * 2 + 40))
            {
                if (o == u || !o.alive || o.flying != u.flying) continue;
                float dx = u.x - o.x, dy = u.y - o.y;
                float minD = (u.radius + o.radius) * (o.IsUnit ? 0.8f : 1f);
                float d2 = dx * dx + dy * dy;
                if (d2 >= minD * minD) continue;
                float d = Mathf.Sqrt(d2);
                if (d < 0.02f) { dx = Random.Range(-1f, 1f); dy = Random.Range(-1f, 1f); d = 1; }
                float strength = (minD - d) / minD, k = o.IsUnit ? 1f : 3f;
                px += dx / d * strength * k;
                py += dy / d * strength * k;
            }
            if (px != 0 || py != 0) Place(u, u.Pos + new Vector2(px, py) * 90 * dt);
        }
    }
}
