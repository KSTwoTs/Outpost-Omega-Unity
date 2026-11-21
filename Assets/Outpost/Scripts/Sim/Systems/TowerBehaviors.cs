using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;
using Outpost.Content;

namespace Outpost.Sim
{
    /// <summary>
    /// The behavior groups that cover every loadout tower (towers.json "behavior"), as in game/rts/sim.py
    /// (ATTACK_BEHAVIORS, SLOW_BEHAVIORS, TAUNT_BEHAVIORS and the branches of update_tower). Battle.Towers runs
    /// each group; all numbers come from the tower's current tier in towers.json.
    /// </summary>
    public static class TowerBehaviors
    {
        public enum Group
        {
            Attack,     // picks a target and fires: projectile, pierce, missile, drones, beam, flak, lightning, chain, cone, mortar, ion, acid, mines
            Slow,       // pulses every 0.25 s on everything in range: aura (cryo), tar, gravity, magnet, fence
            Shockwave,  // sonic: damages and stuns all ground enemies in range
            Disable,    // chrono, hacker: stun the toughest units in range
            Spawn,      // brood: hatches broodlings while enemies are near
            Repair,     // repair: heals friendly units, buildings and towers in range
            Shield,     // shield: tops up shields on friends that haven't been hit for a while
            Buff,       // overclock (+fire rate), generator (+damage), mirror (+range), support / radar (+damage, detects cloaked)
            Income,     // harvester: income for its side
            Bounty,     // biolab: enemy kills in range pay Supplies
            Taunt,      // barricade, bait, decoy: enemy units prefer to shoot them (bait also regenerates)
            Uplink,     // uplink: cuts leader power costs (stored on the faction until powers exist, step 6)
            Garrison,   // bunker: fires with the infantry inside (Battle.Bunkers)
        }

        public static readonly HashSet<string> Attack = new HashSet<string>
            { "projectile", "pierce", "missile", "drones", "beam", "flak", "lightning", "chain", "cone", "mortar", "ion", "acid", "mines" };
        public static readonly HashSet<string> Slow = new HashSet<string> { "aura", "tar", "gravity", "magnet", "fence" };
        public static readonly HashSet<string> Taunt = new HashSet<string> { "barricade", "bait", "decoy" };
        public static readonly HashSet<string> Buff = new HashSet<string> { "overclock", "generator", "mirror", "support" };

        public static Group Of(string behavior)
        {
            if (Attack.Contains(behavior)) return Group.Attack;
            if (Slow.Contains(behavior)) return Group.Slow;
            if (Taunt.Contains(behavior)) return Group.Taunt;
            if (Buff.Contains(behavior)) return Group.Buff;
            switch (behavior)
            {
                case "shockwave": return Group.Shockwave;
                case "chrono":
                case "hacker": return Group.Disable;
                case "brood": return Group.Spawn;
                case "repair": return Group.Repair;
                case "shield": return Group.Shield;
                case "income": return Group.Income;
                case "biolab": return Group.Bounty;
                case "uplink": return Group.Uplink;
                case "garrison": return Group.Garrison;
            }
            return Group.Attack;
        }

        public static readonly string[] Categories = { "assault", "control", "support" };

        /// <summary>
        /// The Python game's default loadout (scenes/rts.py default_loadout): the first two enabled towers of each
        /// category in towers.json order. Used until the loadout screen (Milestone 2).
        /// </summary>
        public static List<string> DefaultLoadout()
        {
            var res = new List<string>();
            foreach (var cat in Categories)
            {
                int n = 0;
                foreach (var p in GameData.Towers.Properties())
                {
                    if (n >= 2 || !(p.Value is JObject d) || GameData.S(d, "category") != cat || !GameData.B(d, "enabled", true)) continue;
                    res.Add(p.Name);
                    n++;
                }
            }
            return res;
        }

        public static readonly string[] CategoryLabels = { "ASSAULT", "CONTROL", "SUPPORT" };

        /// <summary>How many towers of one category a loadout may hold (rts.json towers.per_category, 3).</summary>
        public static int PerCategory => GameData.I(GameData.Rts["towers"], "per_category", 3);

        public static string CategoryOf(string kind) => GameData.S(GameData.Tower(kind), "category");

        /// <summary>The enabled towers of one category, cheapest first (the setup screen's columns, as rts.py).</summary>
        public static List<string> TowersIn(string cat)
        {
            var res = new List<string>();
            foreach (var p in GameData.Towers.Properties())
                if (p.Value is JObject d && GameData.S(d, "category") == cat && GameData.B(d, "enabled", true)) res.Add(p.Name);
            return System.Linq.Enumerable.ToList(System.Linq.Enumerable.OrderBy(res, k => GameData.F(GameData.Tower(k), "cost", 0f)));
        }

        public static int CountIn(List<string> loadout, string cat)
        {
            int n = 0;
            foreach (var k in loadout) if (CategoryOf(k) == cat) n++;
            return n;
        }

        /// <summary>A loadout without unknown, disabled or repeated towers and with at most PerCategory per category (earlier picks win).</summary>
        public static List<string> CleanLoadout(IEnumerable<string> kinds)
        {
            var res = new List<string>();
            if (kinds == null) return res;
            int per = PerCategory;
            foreach (var k in kinds)
            {
                var d = k == null ? null : GameData.Tower(k);
                if (d == null || !GameData.B(d, "enabled", true) || res.Contains(k)) continue;
                string cat = GameData.S(d, "category");
                if (System.Array.IndexOf(Categories, cat) < 0 || CountIn(res, cat) >= per) continue;
                res.Add(k);
            }
            return res;
        }

        /// <summary>
        /// A click on the setup screen (rts.py RTSSetupScene "tower"): a picked tower is removed, otherwise it is added,
        /// dropping the oldest pick of its category when that category is full.
        /// </summary>
        public static void ToggleLoadout(List<string> loadout, string kind)
        {
            if (loadout.Remove(kind)) return;
            var d = GameData.Tower(kind);
            if (d == null) return;
            string cat = GameData.S(d, "category");
            if (CountIn(loadout, cat) >= PerCategory)
                for (int i = 0; i < loadout.Count; i++)
                    if (CategoryOf(loadout[i]) == cat) { loadout.RemoveAt(i); break; }
            loadout.Add(kind);
        }

        /// <summary>Shots per second: beams and flamers tick 5 times a second, the lightning rod fires every 1.7 s.</summary>
        public static float FireRate(string behavior, JToken stats)
        {
            if (behavior == "beam" || behavior == "cone") return 5f;
            if (behavior == "lightning") return 0.6f;
            return GameData.F(stats, "fire_rate", 1f);
        }

        /// <summary>How much a Slow-group tower slows (0..1).</summary>
        public static float SlowAmount(string behavior, JToken stats)
        {
            if (behavior == "magnet") return 0.25f;
            if (behavior == "gravity") return 0.45f;
            return GameData.F(stats, "slow", behavior == "fence" ? 0f : 0.3f);
        }

        /// <summary>Damage per second a Slow-group tower deals (before the 0.25 s tick and the tower damage scale).</summary>
        public static float SlowDps(JToken stats) => GameData.F(stats, "dps") + GameData.F(stats, "burn_dps") * 0.5f;

        /// <summary>Base damage of one shot of an Attack-group tower (before TowerDamage scaling).</summary>
        public static float ShotDamage(string behavior, JToken stats)
        {
            switch (behavior)
            {
                case "projectile": return GameData.F(stats, "damage", 8);
                case "drones": return GameData.F(stats, "damage", 6) * Mathf.Max(1, GameData.I(stats, "drones", 2));
                case "pierce": return GameData.F(stats, "damage", 60);
                case "missile":
                case "flak": return GameData.F(stats, "damage", 20);
                case "beam": return GameData.F(stats, "dps", 10) * 0.2f;
                case "chain":
                case "lightning": return GameData.F(stats, "damage", GameData.F(stats, "max_charge", 100) * 0.35f);
                case "cone": return GameData.F(stats, "damage", 6) * GameData.F(stats, "fire_rate", 6) / 5f + GameData.F(stats, "burn_dps", 6) * 0.1f;
                case "mortar":
                case "mines": return GameData.F(stats, "damage", 40);
                case "ion": return GameData.F(stats, "damage", 300);
                case "acid": return GameData.F(stats, "dps", 8);
            }
            return GameData.F(stats, "damage");
        }
    }
}
