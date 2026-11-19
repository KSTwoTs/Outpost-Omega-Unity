using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;
using Outpost.Content;

namespace Outpost.Sim
{
    /// <summary>
    /// Production: unit queues and cancelling, spawning units with their research / leader / difficulty multipliers,
    /// the hero (Commander / Queen) spawn, and buildings working through their queues. The respawn timer itself runs in Economy().
    /// </summary>
    public partial class Battle
    {
        public string QueueUnit(Building b, string kind)
        {
            var f = factions[b.faction];
            var d = GameData.Unit(kind);
            if (d == null || System.Array.IndexOf(b.trains, kind) < 0) return "Can't train that here";
            if (!b.Done) return "Still building";
            if (b.queue.Count >= 5) return "Queue full";
            if (f.tech < GameData.I(d, "tech", 1)) return $"Needs Tech {GameData.I(d, "tech", 1)}";
            int pop = GameData.I(d, "pop", 1);
            if (f.popUsed + pop > f.popCap) return "Population cap reached";
            int cost = GameData.I(d, "cost", 100);
            if (!Pay(b.faction, cost)) return "Not enough " + f.ResName;
            float t = GameData.F(d, "time", 10);
            b.queue.Add(new QueueItem { id = kind, t = t, total = t, cost = cost, pop = pop });
            f.popUsed += pop;
            return null;
        }

        public void CancelQueue(Building b, int index)
        {
            if (index < 0 || index >= b.queue.Count) return;
            var q = b.queue[index];
            b.queue.RemoveAt(index);
            factions[b.faction].res += q.cost;
            factions[b.faction].popUsed -= q.pop;
            if (q.type == "research") factions[b.faction].researching.Remove(q.id);
        }

        public Unit SpawnUnit(string kind, string faction, float x, float y, string model = null)
        {
            var mods = UnitMods(faction, kind);
            var u = new Unit(kind, faction, x, y, mods.hp) { model = model };
            u.speed *= mods.spd;
            u.range *= mods.rng;
            var owner = factions[faction];
            if (owner.hero == null && GameData.F(u.data, "respawn") > 0) owner.hero = u;   // Commander / Queen
            if (!u.flying)
            {
                var p = grid.NearestWalkablePx(new Vector2(x, y));
                u.x = p.x;
                u.y = p.y;
                u.guard = u.lastPos = p;
            }
            units.Add(u);
            fx?.Spawned(u);
            return u;
        }

        /// <summary>
        /// (hp, damage, speed, range) multipliers: research and the leader passive for the player;
        /// tech level, evolutions and difficulty for the aliens.
        /// </summary>
        public (float hp, float dmg, float spd, float rng) UnitMods(string faction, string kind)
        {
            float hp = 1f, dmg = 1f, spd = 1f, rng = 1f;
            var done = factions[faction].research;
            var d = GameData.Unit(kind);
            string cls = GameData.S(d, "class", "inf");
            if (faction == "player")
            {
                var L = GameData.Rts["leaders"]?[leader];
                if (kind == "commander")
                {
                    hp *= GameData.F(L, "hero_hp", 1f);
                    dmg *= GameData.F(L, "hero_damage", 1f);
                }
                dmg *= GameData.F(L?["unit_damage"], kind, 1f) * GameData.F(L?["class_damage"], cls, 1f);
                rng *= GameData.F(L?["unit_range"], kind, 1f);
                if (cls == "inf" && done.Contains("inf_armor")) hp *= 1.25f;
                if (cls == "veh" && done.Contains("veh_armor")) hp *= 1.2f;
                if (cls == "air" && done.Contains("afterburners")) spd *= 1.25f;
                if (cls == "air" && done.Contains("air_weapons")) dmg *= 1.2f;
                if (done.Contains("weapons1")) dmg *= 1.15f;
                if (done.Contains("weapons2")) dmg *= 1.15f;
                if ((kind == "commandos" || kind == "snipers") && done.Contains("jump_jets")) spd *= 1.2f;
            }
            else if (faction == "alien")
            {
                // aliens evolve with tech level instead of research
                int tech = factions[faction].tech;
                hp *= 1f + 0.08f * (tech - 1);
                dmg *= 1f + 0.08f * (tech - 1);
                hp *= 0.85f + 0.15f * alienArmyMult;
                if (done.Contains("carapace")) hp *= 1.2f;
                if (done.Contains("adrenal"))
                {
                    dmg *= 1.15f;
                    spd *= 1.15f;
                }
            }
            return (hp, dmg, spd, rng);
        }

        /// <summary>Brings a faction's hero (Commander / Queen) in beside its first base.</summary>
        public Unit SpawnHero(string faction)
        {
            var b = bases.FirstOrDefault(x => x.Alive && x.faction == faction);
            if (b == null) return null;
            var f = factions[faction];
            f.hero = null;
            var u = SpawnUnit(faction == "player" ? "commander" : "queen", faction, b.x, b.y + 108);
            f.hero = u;
            return u;
        }

        void UpdateBuilding(Building b, float dt)
        {
            b.TickStatus(dt);
            if (b.scanCd > 0) b.scanCd -= dt;
            if (!b.Done)
            {
                b.buildLeft -= dt;
                b.hp = Mathf.Min(b.maxHp, b.hp + b.maxHp * 0.9f * dt / b.buildTotal);
                return;
            }
            if (b.queue.Count == 0) return;
            var q = b.queue[0];
            q.t -= dt;
            if (q.t > 0) return;
            b.queue.RemoveAt(0);
            var f = factions[b.faction];
            if (q.type == "research")
            {
                f.researching.Remove(q.id);
                f.research.Add(q.id);
                ApplyResearch(b.faction, q.id);
                if (b.faction == "player")
                    fx?.Message("player", "Research complete: " + GameData.S(b.Research?[q.id], "name", q.id));
                return;
            }
            if (q.type == "upgrade")
            {
                b.upgraded = true;
                float extra = GameData.F(b.Upgrade, "hp");
                b.maxHp += extra;
                b.hp += extra;
                b.name = GameData.S(b.Upgrade, "name", b.name);
                fx?.Ring(b.Pos, b.radius + 20, "upgrade");
                if (b.faction == "player") fx?.Message("player", b.name + " upgrade complete");
                return;
            }
            f.popUsed -= q.pop;
            var bs = b.baseRef;
            var dir = new Vector2(b.x - bs.x, b.y - bs.y);
            if (dir.sqrMagnitude < 1) dir = Vector2.right;
            dir.Normalize();
            var sp = b.Pos + dir * (b.radius + 22);
            var u = SpawnUnit(q.id, b.faction, sp.x, sp.y);
            if (b.rally.HasValue) OrderMove(new[] { u }, b.rally.Value, true);
            else OrderMove(new[] { u }, sp + dir * 40, false);
            if (b.faction == "alien") ai?.OnUnit(u);
        }
    }
}
