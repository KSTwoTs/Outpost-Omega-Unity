using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;
using Outpost.Content;

namespace Outpost.Sim
{
    /// <summary>
    /// Construction: founding bases, placing buildings and turrets, costs, building upgrades (Fortress / Elder Hive...)
    /// and research (queueing it and applying it to units and towers already in the field).
    /// </summary>
    public partial class Battle
    {
        // ------------------------------------------------------------------ setup / construction
        public Base FoundBase(MapData.SiteData site, string faction, bool instant)
        {
            var b = new Base
            {
                faction = faction, site = site,
                slots = new Building[site.slots.Length], sockets = new Tower[site.sockets.Length],
            };
            var hq = new Building(faction == "player" ? "hq" : "hive", faction, site.x, site.y, b, -1);
            ScaleHp(hq);
            if (instant) Finish(hq);
            b.hq = hq;
            bases.Add(b);
            buildings.Add(hq);
            grid.BlockCircle(site.x, site.y, 44, 1);
            fx?.Spawned(hq);
            return b;
        }

        static void Finish(Entity e)
        {
            if (e is Building b) b.buildLeft = 0;
            if (e is Tower t) t.buildLeft = 0;
            e.hp = e.maxHp;
        }

        public Building PlaceBuilding(Base b, int slot, string kind, bool instant)
        {
            var p = b.site.slots[slot];
            var bl = new Building(kind, b.faction, p[0], p[1], b, slot);
            ScaleHp(bl);
            if (instant) Finish(bl);
            b.slots[slot] = bl;
            buildings.Add(bl);
            grid.BlockCircle(bl.x, bl.y, 26, 1);
            fx?.Spawned(bl);
            return bl;
        }

        public Tower PlaceTower(Base b, int socket, string kind, bool instant)
        {
            var p = b.site.sockets[socket];
            // Turret Plating toughens loadout towers (not Bunkers or Spines), as sim.py build_tower
            bool plated = kind != "spine" && kind != "bunker" && factions[b.faction].research.Contains("turret_plating");
            // and the leader's building_hp (bastion) toughens them too, as sim.py build_tower
            float hpm = (plated ? 1.4f : 1f) * (kind != "spine" && kind != "bunker" ? BuildingHpMult(b.faction) : 1f);
            var t = new Tower(kind, b.faction, p[0], p[1], b, socket, hpm);
            t.angle = Mathf.Atan2(p[1] - b.y, p[0] - b.x);
            if (instant) Finish(t);
            b.sockets[socket] = t;
            towers.Add(t);
            grid.BlockCircle(t.x, t.y, 14, 1);
            fx?.Spawned(t);
            return t;
        }

        /// <summary>A building's price; the player's leader may make it cheaper (engineer -15%).</summary>
        public int BuildCost(string kind, string faction = "player")
        {
            int c = GameData.I(GameData.Building(kind), "cost", 100);
            return faction == "player" ? Cost(c, LeaderF("build_cost", 1f)) : c;
        }

        /// <summary>Leader building_hp (bastion) on a new player building.</summary>
        void ScaleHp(Building bl)
        {
            float m = BuildingHpMult(bl.faction);
            bl.maxHp *= m;
            bl.hp *= m;
        }

        public int TowerCost(string kind) =>
            kind == "spine" ? GameData.I(GameData.Rts["alien_tower"], "cost", 150)
                : kind == "bunker" ? GameData.I(GameData.Rts["bunker"], "cost", 200)
                : Cost(GameData.F(GameData.Tower(kind), "cost", 100) * GameData.F(GameData.Rts["towers"], "cost_mult", 2f), LeaderF("build_cost", 1f));

        bool Pay(string faction, int amount)
        {
            var f = factions[faction];
            if (f.res + 1e-3f < amount) return false;
            f.res -= amount;
            return true;
        }

        public int FreeSlot(Base b)
        {
            for (int i = 0; i < b.SlotLimit; i++) if (b.slots[i] == null) return i;
            return -1;
        }

        public int FreeSocket(Base b)
        {
            for (int i = 0; i < b.sockets.Length; i++) if (b.sockets[i] == null) return i;
            return -1;
        }

        /// <summary>Starts a building in the base's next free slot. Returns an error message or null.</summary>
        public string Build(Base b, string kind)
        {
            if (!b.Alive) return "Base lost";
            if (!b.hq.Done) return "Base still building";
            int slot = FreeSlot(b);
            if (slot < 0) return "No free building slots";
            var d = GameData.Building(kind);
            if (d == null) return "Unknown building";
            if (factions[b.faction].tech < BuildTech(kind)) return $"Needs Tech {BuildTech(kind)}";
            if ((kind == "reactor" || kind == "node") &&
                buildings.Count(x => x.alive && x.faction == b.faction && x.kind == kind) >= 2)
                return "Max 2 (Tech 3 is the cap)";
            if (!Pay(b.faction, BuildCost(kind, b.faction))) return "Not enough " + factions[b.faction].ResName;
            PlaceBuilding(b, slot, kind, false);
            return null;
        }

        /// <summary>Tech level a building needs (Air Pad and Flyer Spire need Tech 2).</summary>
        public int BuildTech(string kind) =>
            GameData.I(GameData.Building(kind), "tech_req", kind == "airpad" || kind == "spire" ? 2 : 1);

        /// <summary>Starts a building's upgrade (Fortress / Elder Hive: 7 slots and more HP; supply: more income).</summary>
        public string QueueUpgrade(Building b)
        {
            var up = b.Upgrade;
            var f = factions[b.faction];
            if (up == null || b.upgraded) return "No upgrade";
            if (b.Upgrading) return "Already upgrading";
            if (f.tech < GameData.I(up, "tech", 1)) return $"Needs Tech {GameData.I(up, "tech", 1)}";
            if (!b.Done) return "Still building";
            int cost = GameData.I(up, "cost", 200);
            if (!Pay(b.faction, cost)) return "Not enough " + f.ResName;
            float t = GameData.F(up, "time", 30);
            b.queue.Add(new QueueItem { type = "upgrade", id = "up", t = t, total = t, cost = cost, pop = 0 });
            return null;
        }

        public string BuildTower(Base b, string kind)
        {
            if (!b.Alive) return "Base lost";
            if (!b.hq.Done) return "Base still building";
            int s = FreeSocket(b);
            if (s < 0) return "No free turret sockets";
            bool human = b.faction != "alien" && b.faction != "wild";
            if (human ? kind == "spine" : kind != "spine") return "Can't build that";
            if (kind != "spine" && kind != "bunker" && GameData.Tower(kind) == null) return "Unknown tower";
            int cost = TowerCost(kind);
            if (!Pay(b.faction, cost)) return "Not enough " + factions[b.faction].ResName;
            PlaceTower(b, s, kind, false).costPaid = cost;
            return null;
        }

        /// <summary>Starts a research at the building that offers it (rts.json buildings.*.research).</summary>
        public string QueueResearch(Building b, string rid)
        {
            var f = factions[b.faction];
            var all = b.Research;
            var r = all?[rid];
            if (r == null) return "Unknown research";
            if (f.research.Contains(rid) || f.researching.Contains(rid)) return "Already researched";
            string after = GameData.S(r, "after");
            if (after != null && !f.research.Contains(after)) return "Research " + GameData.S(all[after], "name", after) + " first";
            if (f.tech < GameData.I(r, "tech", 1)) return $"Needs Tech {GameData.I(r, "tech", 1)}";
            if (!b.Done) return "Still building";
            if (b.queue.Count >= 5) return "Queue full";
            int cost = GameData.I(r, "cost", 200);
            if (!Pay(b.faction, cost)) return "Not enough " + f.ResName;
            float t = GameData.F(r, "time", 30);
            f.researching.Add(rid);
            b.queue.Add(new QueueItem { type = "research", id = rid, t = t, total = t, cost = cost, pop = 0 });
            return null;
        }

        /// <summary>A finished research takes effect on units and towers already in the field.</summary>
        void ApplyResearch(string faction, string rid)
        {
            float vetHp = GameData.F(GameData.Rts["veterancy"], "hp", 0.1f);
            foreach (var u in units)
            {
                if (!u.alive || u.faction != faction || u.data == null) continue;
                var m = UnitMods(faction, u.kind);
                float member = GameData.F(u.data, "hp", 50) * m.hp * (1 + vetHp * u.rank);
                if (Mathf.Abs(member - u.memberHp) > 0.01f)
                {
                    float ratio = u.Ratio;
                    u.memberHp = member;
                    u.maxHp = member * u.squad;
                    u.hp = u.maxHp * ratio;
                }
                u.speed = GameData.F(u.data, "speed", 60) * m.spd;
                u.range = GameData.F(u.data, "range", 100) * m.rng;
            }
            if (rid == "turret_plating")
                foreach (var t in towers)
                    if (t.alive && t.faction == faction)
                    {
                        t.hpMult *= 1.4f;
                        t.ApplyTier();
                    }
        }
    }
}
