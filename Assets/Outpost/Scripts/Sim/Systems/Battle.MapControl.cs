using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Outpost.Content;

namespace Outpost.Sim
{
    /// <summary>
    /// Map control: the standard Skirmish start, wild nests, expansions, relays, supply crates and
    /// recycling buildings (game/rts/sim.py setup, expand, demolish, update_relays, update_crates).
    /// </summary>
    public partial class Battle
    {
        public readonly List<Relay> relays = new List<Relay>();
        public readonly List<Crate> crates = new List<Crate>();
        /// <summary>Ids of the expansion sites that started with a wild nest guarding them.</summary>
        public readonly HashSet<int> wildSites = new HashSet<int>();
        float relayT;

        void SetupMapControl()
        {
            factions["wild"] = new Faction { name = "wild" };
            if (map.relays != null)
                foreach (var r in map.relays) relays.Add(new Relay { x = r[0], y = r[1] });
            var cfg = GameData.Rts["crates"];
            int lo = GameData.I(cfg, "min", 60), hi = GameData.I(cfg, "max", 160);
            if (map.crates != null)
                foreach (var c in map.crates) crates.Add(new Crate { x = c[0], y = c[1], value = Random.Range(lo, hi + 1) });
        }

        // ------------------------------------------------------------------ the standard start
        /// <summary>
        /// Sets up a Skirmish the way the Python game does: both main bases with their first income
        /// building, the starting squads, the Commander, the alien AI, and wild nests guarding half
        /// of the expansion sites (in mirrored pairs, so it stays fair).
        /// </summary>
        public void StandardStart(string difficulty = "Normal")
        {
            ai = new AlienAI(this, difficulty);
            var st = GameData.Rts["start"];
            foreach (var site in map.sites)
            {
                if (site.main != "player" && site.main != "alien") continue;
                bool human = site.main == "player";
                var b = FoundBase(site, site.main, true);
                if (b.slots.Length > 3) PlaceBuilding(b, 3, human ? "supply" : "pool", true);
                var kinds = GameData.List(st, human ? "player_units" : "alien_units");
                for (int i = 0; i < kinds.Length; i++)
                    if (GameData.Unit(kinds[i]) != null)
                        SpawnUnit(kinds[i], site.main, site.x + (human ? 90 + i * 30 : -90 - i * 30), site.y + (human ? -120 : 120));
                if (human) SpawnHero("player");
            }

            var guards = GameData.List(GameData.Rts["ai"], "wild_guards");
            var free = map.sites.Where(s => s.main != "player" && s.main != "alien").ToList();
            for (int i = 0; i + 1 < free.Count && guards.Length > 0; i += 2)
            {
                if ((i / 2) % 2 != 0) continue;
                foreach (var s in new[] { free[i], free[i + 1] })
                {
                    wildSites.Add(s.id);
                    for (int j = 0; j <= guards.Length; j++)
                    {
                        string kind = guards[j % guards.Length];
                        if (GameData.Unit(kind) == null) continue;
                        float a = j * Mathf.PI * 2f / 3f;
                        var u = SpawnUnit(kind, "wild", s.x + 60 * Mathf.Cos(a), s.y + 60 * Mathf.Sin(a), WildModel(kind));
                        u.leash = new Vector3(s.x, s.y, 330);
                        u.name = "Wild " + u.name;
                    }
                }
            }
            Recount();
            UpdateFog();
        }

        /// <summary>Wild nests are the Fungal Bloom: same stats as the alien unit, their own look.</summary>
        static string WildModel(string kind)
        {
            var d = GameData.Unit(kind);
            string cls = GameData.S(d, "class", "inf");
            if (cls != "inf") return "bloom_titan";
            return GameData.F(d, "hp", 30) * Mathf.Max(1, GameData.I(d, "squad", 1)) >= 200 || GameData.I(d, "squad", 1) <= 3 ? "shambler" : "sporeling";
        }

        // ------------------------------------------------------------------ expansions
        /// <summary>The living base on a site, or null if the site is free.</summary>
        public Base BaseAt(MapData.SiteData site) => bases.FirstOrDefault(b => b.site == site && b.Alive);

        public IEnumerable<MapData.SiteData> FreeSites() => map.sites.Where(s => BaseAt(s) == null);

        public int ExpansionCost(string faction)
        {
            int c = GameData.I(GameData.Rts["expansion_cost"], faction == "player" ? "player" : "alien", 600);
            return faction == "player" ? Cost(c, LeaderF("build_cost", 1f)) : c;
        }

        /// <summary>Why this faction can't build a base here right now, or null if it can.</summary>
        public string CanExpand(string faction, MapData.SiteData site)
        {
            if (BaseAt(site) != null) return "Site taken";
            if (EnemiesNear(faction, site.x, site.y, 260).Any(e => !e.IsBuilding && !e.IsTower)) return "Clear the enemies first";
            bool mine = false;
            foreach (var e in Near(site.x, site.y, 420))
                if (e.alive && e.IsUnit && e.faction == faction && D2(e.x, e.y, site.x, site.y) <= 420 * 420) { mine = true; break; }
            if (!mine) return "Need units nearby";
            if (factions[faction].res < ExpansionCost(faction)) return "Not enough " + factions[faction].ResName;
            return null;
        }

        /// <summary>Starts a new base on a free site (the core builds over time). Returns an error message or null.</summary>
        public string Expand(string faction, MapData.SiteData site)
        {
            string problem = CanExpand(faction, site);
            if (problem != null) return problem;
            Pay(faction, ExpansionCost(faction));
            FoundBase(site, faction, false);
            wildSites.Remove(site.id);
            if (faction == "alien") fx?.Message("alien", "The aliens are growing a new Hive");
            return null;
        }

        /// <summary>Recycles a building for half its cost (all of it while still under construction).</summary>
        public string Demolish(Building b)
        {
            if (b == null || !b.alive) return "Nothing to recycle";
            if (b.IsHq) return "Can't recycle a base core";
            int refund = Mathf.RoundToInt(BuildCost(b.kind, b.faction) * (b.Done ? 0.5f : 1f));
            while (b.queue.Count > 0) CancelQueue(b, 0);
            factions[b.faction].res += refund;
            Kill(b, null);
            return null;
        }

        // ------------------------------------------------------------------ relays and crates
        void UpdateMapControl(float dt)
        {
            relayT -= dt;
            if (relayT > 0) return;
            float step = 0.25f - relayT;
            relayT = 0.25f;
            UpdateRelays(step);
            UpdateCrates();
        }

        /// <summary>Units standing on a relay capture it: +income (and faster leader powers later).</summary>
        void UpdateRelays(float dt)
        {
            var cfg = GameData.Rts["relays"];
            float radius = GameData.F(cfg, "radius", 90), rate = dt / Mathf.Max(0.1f, GameData.F(cfg, "capture_time", 8));
            foreach (var r in relays)
            {
                bool player = false, alien = false;
                foreach (var e in Near(r.x, r.y, radius))
                {
                    if (!e.alive || !e.IsUnit || D2(e.x, e.y, r.x, r.y) > (radius + e.radius) * (radius + e.radius)) continue;
                    if (e.faction == "player") player = true;
                    else if (e.faction == "alien") alien = true;
                }
                r.contested = player && alien;
                if (player == alien) continue;                       // nobody there, or both sides
                string who = player ? "player" : "alien";
                float sign = player ? 1f : -1f;
                if (r.owner != null && r.owner != who)
                {
                    r.progress -= (r.owner == "player" ? 1f : -1f) * rate * 1.5f;      // drain the old owner first
                    if (r.progress * (r.owner == "player" ? 1f : -1f) <= 0)
                    {
                        r.owner = null;
                        r.progress = 0;
                    }
                }
                else
                {
                    r.progress = Mathf.Clamp(r.progress + sign * rate, -1f, 1f);
                    if (Mathf.Abs(r.progress) >= 1f && r.owner != who)
                    {
                        r.owner = who;
                        if (who == "player") Note(new Vector2(r.x, r.y), "Relay captured: more income", "player");
                        else Alert(new Vector2(r.x, r.y), "The aliens took a relay", 5f);
                    }
                }
            }
        }

        void UpdateCrates()
        {
            foreach (var c in crates)
            {
                if (!c.alive) continue;
                foreach (var e in Near(c.x, c.y, 26))
                {
                    if (!e.alive || !e.IsUnit || (e.faction != "player" && e.faction != "alien")) continue;
                    if (D2(e.x, e.y, c.x, c.y) > (26 + e.radius) * (26 + e.radius)) continue;
                    c.alive = false;
                    factions[e.faction].res += c.value;
                    if (e.faction == "player")
                    {
                        fx?.Message("player", $"Supply crate: +{c.value} Supplies");
                        fx?.Float(new Vector2(c.x, c.y), $"+{c.value} Supplies", "supplies");
                    }
                    break;
                }
            }
        }

        /// <summary>Income from relays this faction holds, per second (before its income multiplier).</summary>
        float RelayIncome(string faction)
        {
            int n = 0;
            foreach (var r in relays) if (r.owner == faction) n++;
            return n * GameData.F(GameData.Rts["relays"], "income", 0.6f);
        }

        /// <summary>Wild nest guards stay home: past the leash they drop everything, walk back and heal.</summary>
        bool Leashed(Unit u, float dt)
        {
            if (!u.leash.HasValue) return false;
            var l = u.leash.Value;
            if (D2(u.x, u.y, l.x, l.y) <= l.z * l.z) return false;
            u.target = null;
            if (u.order != Order.Move || u.attackMove) OrderMove(new[] { u }, new Vector2(l.x, l.y), false);
            u.hp = Mathf.Min(u.maxHp, u.hp + u.maxHp * 0.2f * dt);
            return true;
        }
    }
}
