using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;
using Outpost.Content;

namespace Outpost.Sim
{
    /// <summary>
    /// Bunkers (rts.json "bunker"): built on a tower socket; up to "capacity" infantry squads walk in, leave the
    /// field, and the Bunker shoots with their combined damage x damage_bonus at its own range. Selling lets them
    /// out unhurt; if the Bunker is destroyed they come out with half their health gone. Port of sim.py
    /// build_bunker, order_garrison, enter_bunker, unload and bunker_fire.
    /// </summary>
    public partial class Battle
    {
        /// <summary>Infantry and heroes (not the Commander) can garrison.</summary>
        public static bool CanGarrison(Unit u) =>
            u != null && u.alive && u.inBunker == null && (u.cls == "inf" || u.cls == "hero") && u.kind != "commander";

        /// <summary>Sends the squads that fit to the Bunker. Returns how many of the given units could garrison.</summary>
        public int OrderGarrison(IEnumerable<Unit> units, Tower bunker)
        {
            if (bunker == null || !bunker.alive || !bunker.IsBunker) return 0;
            var ok = units.Where(u => CanGarrison(u) && u.faction == bunker.faction).ToList();
            int room = Mathf.Max(0, bunker.capacity - bunker.garrison.Count);
            foreach (var u in ok.Take(room))
            {
                u.order = Order.Garrison;
                u.target = bunker;
                u.attackMove = false;
                u.goal = null;
                u.path.Clear();
            }
            return ok.Count;
        }

        /// <summary>A squad on its way into a Bunker. False when the order fell through and the normal update should run.</summary>
        bool UpdateGarrisonOrder(Unit u, float dt)
        {
            var b = u.target as Tower;
            if (b == null || !b.alive || !b.IsBunker || b.garrison.Count >= b.capacity)
            {
                u.order = Order.None;
                u.target = null;
                u.guard = u.Pos;
                return false;
            }
            float reach = b.radius + u.radius + 14;
            if (D2(u.x, u.y, b.x, b.y) <= reach * reach)
            {
                EnterBunker(u, b);
                return true;
            }
            Chase(u, b, dt);
            Separate(u, dt);
            u.lastPos = u.Pos;
            return true;
        }

        void EnterBunker(Unit u, Tower b)
        {
            u.order = Order.None;
            u.target = null;
            u.path.Clear();
            u.goal = null;
            u.inBunker = b;          // Tick drops it from the units list
            b.garrison.Add(u);
            fx?.Ring(b.Pos, 24, "energy");
        }

        /// <summary>Lets every squad out around the Bunker; hurt (0..1) takes that share of their health.</summary>
        public void Unload(Tower b, float hurt = 0f)
        {
            for (int i = 0; i < b.garrison.Count; i++)
            {
                var u = b.garrison[i];
                float a = i * 2.1f;
                var p = grid.NearestWalkablePx(new Vector2(b.x + 40 * Mathf.Cos(a), b.y + 40 * Mathf.Sin(a)), 4);
                u.x = p.x;
                u.y = p.y;
                u.guard = u.lastPos = p;
                u.hp -= u.maxHp * hurt;
                u.inBunker = null;
                u.order = Order.None;
                u.target = null;
                if (u.hp > 0)
                {
                    if (!units.Contains(u)) units.Add(u);
                }
                else
                {
                    u.alive = false;
                    u.hp = 0;
                    factions[u.faction].losses++;
                    units.Remove(u);
                    fx?.Died(u);
                }
            }
            b.garrison.Clear();
        }

        void BunkerFire(Tower t, float dt)
        {
            if (t.garrison.Count == 0) return;
            float rng = t.range * t.buffRange;
            bool air = t.garrison.Any(u => u.hitsAir);
            t.retarget -= dt;
            if (t.target == null || !t.target.alive || t.retarget <= 0)
            {
                t.retarget = 0.3f;
                t.target = TowerTarget(t, rng, air);
            }
            var tg = t.target;
            if (tg == null || t.cooldown > 0) return;
            t.cooldown = 0.5f;
            float vet = GameData.F(GameData.Rts["veterancy"], "damage", 0.15f), dps = 0f;
            foreach (var u in t.garrison)
            {
                float dm = UnitMods(u.faction, u.kind).dmg * (1 + vet * u.rank);
                if (u.damage > 0 && (u.hitsAir || !tg.flying)) dps += u.damage * u.Members * u.rate * dm;
            }
            t.angle = Mathf.Atan2(tg.y - t.y, tg.x - t.x);
            Hit(tg, dps * 0.5f * GameData.F(t.data, "damage_bonus", 1.25f), "inf", t, t.faction);
            fx?.Shot(t, t.Pos, tg.Pos, t.faction, Mathf.Min(3, t.garrison.Count));
        }
    }
}
