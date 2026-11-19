using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Outpost.Sim
{
    /// <summary>
    /// The alien AI's economy: the shopping list (build plans, Spines, Pool and Hive upgrades, expansions) with its
    /// savings fund, unit production with counter-picks, and evolutions (research).
    /// </summary>
    public partial class AlienAI
    {
        // what the Swarm builds to beat each of the player's unit classes
        static readonly Dictionary<string, string[]> CounterPicks = new Dictionary<string, string[]>
        {
            { "inf", new[] { "juggernaut", "brutes", "grub", "titan", "leeches", "rollers", "ravager" } },
            { "veh", new[] { "gliders", "bomber", "burrowers", "brutes", "leeches" } },
            { "air", new[] { "spitters", "spitters", "carrier", "cloakers" } },
            { "hero", new[] { "brutes", "spitters", "cloakers" } },
        };
        static readonly string[] Support = { "menders", "shield_drone" };

        // what each new expansion grows, in the order the Swarm claims them
        static readonly string[][] ExpansionOrders =
        {
            new[] { "pool", "node", "den", "nest" }, new[] { "pool", "cradle", "pool", "spire" }, new[] { "pool", "nest", "pool", "pit" },
        };

        // ------------------------------------------------------------------ economy
        /// <summary>The next building a base wants from its plan (null when the plan is complete or blocked).</summary>
        string NextBuilding(Base bs, string[] plan, bool main)
        {
            var have = bs.slots.Take(bs.SlotLimit).Where(s => s != null).Select(s => s.kind).ToList();
            var counts = new Dictionary<string, int>();
            foreach (var k in plan)
            {
                counts.TryGetValue(k, out int n);
                counts[k] = ++n;
                if (have.Count(h => h == k) >= n) continue;
                if (GameData.Building(k) == null) return null;
                // a third Synapse Node is never needed: move on to the rest of the plan
                if (k == "node" && battle.buildings.Count(b => b.alive && b.faction == "alien" && b.kind == "node") >= 2) continue;
                return battle.Alien.tech >= battle.BuildTech(k) ? k : null;       // wait for the tech it needs
            }
            if (!main) return null;
            // spare slots in the main Hive after the plan: stealth breeds, then more income
            if (GameData.Building("den") != null && !have.Contains("den") && battle.Alien.tech >= battle.BuildTech("den")) return "den";
            return "pool";
        }

        /// <summary>Is it time for another Hive? The first at the difficulty's expand_at, then one every four minutes, up to four bases.</summary>
        bool ExpansionDue(List<Base> bases)
        {
            if (battle.time < expandAt || bases.Count >= 4) return false;
            return bases.Count < 2 || battle.time >= expandAt + 240 * (bases.Count - 1);
        }

        /// <summary>The free site the Swarm wants next: the one nearest its main Hive.</summary>
        Outpost.Content.MapData.SiteData NextSite(Base home) =>
            battle.FreeSites().OrderBy(s => (new Vector2(s.x, s.y) - new Vector2(home.x, home.y)).sqrMagnitude).FirstOrDefault();

        /// <summary>
        /// Buys the next thing on the list, or puts a share of income aside for it (more for things that
        /// raise income, since they pay for themselves). Saving only starts once there is a guard force,
        /// and the savings are spent on defense while a base is under attack.
        /// </summary>
        void Economy(List<Base> bases, Faction f, List<Unit> army, bool threat)
        {
            reserve = 0;
            saving = null;
            var home = bases[0];
            int wantSpines = battle.time > 600 ? 4 : 2;
            bool spineTime = battle.time >= 150;

            // the shopping list, in priority order: (name, cost, guard force needed before saving for it, how to buy it)
            var goals = new List<(string id, int cost, int guard, System.Func<bool> buy)>();
            var later = new List<(string id, int cost, int guard, System.Func<bool> buy)>();
            for (int i = 0; i < bases.Count; i++)
            {
                var bs = bases[i];
                if (!bs.hq.Done) continue;
                bool main = i == 0;
                int built = bs.slots.Count(s => s != null);
                int spines = bs.sockets.Count(t => t != null);
                bool socket = spineTime && battle.FreeSocket(bs) >= 0;
                if (socket && spines == 0 && built >= 3) goals.Add(("spine", battle.TowerCost("spine"), 5, () => battle.BuildTower(bs, "spine") == null));
                string want = battle.FreeSlot(bs) >= 0 ? NextBuilding(bs, main ? buildOrder : ExpansionOrders[(i - 1) % ExpansionOrders.Length], main) : null;
                if (want != null) goals.Add((want, battle.BuildCost(want), main ? Mathf.Min(2 + built, 8) : 5, () => battle.Build(bs, want) == null));
                if (socket && spines < wantSpines) later.Add(("spine", battle.TowerCost("spine"), 8, () => battle.BuildTower(bs, "spine") == null));
            }
            var site = ExpansionDue(bases) ? NextSite(home) : null;
            if (site != null) goals.Add(("expand", battle.ExpansionCost("alien"), 6, () => battle.Expand("alien", site) == null));
            var pool = battle.buildings.FirstOrDefault(b => b.alive && b.faction == "alien" && b.kind == "pool" && b.Done &&
                                                            !b.upgraded && !b.Upgrading && b.Upgrade != null);
            if (pool != null && home.slots.Count(s => s != null) >= 4)
                goals.Add(("pool_upgrade", GameData.I(pool.Upgrade, "cost", 220), 6, () => battle.QueueUpgrade(pool) == null));
            goals.AddRange(later);
            var hq = home.hq;
            if (hq.Done && !hq.upgraded && !hq.Upgrading && hq.Upgrade != null && f.tech >= GameData.I(hq.Upgrade, "tech", 2) && battle.time > 200)
                goals.Add(("hq_upgrade", GameData.I(hq.Upgrade, "cost", 600), 6, () => battle.QueueUpgrade(hq) == null));
            if (goals.Count == 0)
            {
                fund = 0;
                return;
            }

            var g = goals[0];
            if (f.res >= g.cost)
            {
                // an expansion can be affordable and still blocked (wild nest, no escort there yet): keep the money for it
                if (g.buy()) fund = 0;
                else if (g.id == "expand") { reserve = Mathf.Min(g.cost, f.res); saving = g.id; }
                return;
            }
            if (threat || army.Count < g.guard) return;      // the fund stays, but everything is spendable
            bool pays = g.id == "pool" || g.id == "pool_upgrade" || g.id == "expand";
            fund = Mathf.Min(Mathf.Min(fund + f.income * (pays ? 0.75f : 0.45f), g.cost), f.res);
            reserve = fund;
            saving = g.id;
        }

        // ------------------------------------------------------------------ production
        readonly Dictionary<int, string> wanted = new Dictionary<int, string>();   // building id -> unit it is saving for
        readonly Dictionary<int, float> waitSince = new Dictionary<int, float>();

        /// <summary>
        /// Every idle production building picks a unit (leaning toward counters to the player's army)
        /// and waits until it can afford it, instead of settling for the cheapest thing. Buildings
        /// take turns: whoever has waited longest has first call on the Biomass, so the big beasts
        /// and flyers get built as well as the swarm. Under attack they take whatever they can afford.
        /// </summary>
        void Produce(Faction f, List<Unit> army, bool threat)
        {
            var mix = new Dictionary<string, int> { { "inf", 0 }, { "veh", 0 }, { "air", 0 }, { "hero", 0 } };
            foreach (var u in battle.units)
                if (u.alive && u.faction == "player")
                {
                    mix.TryGetValue(u.cls, out int n);
                    mix[u.cls] = n + u.pop;
                }
            int total = Mathf.Max(1, mix.Values.Sum());
            string top = new[] { "inf", "veh", "air" }.OrderByDescending(c => mix[c]).First();
            var producers = battle.buildings.Where(b => b.alive && b.faction == "alien" && b.Done && b.trains.Length > 0).ToList();
            foreach (var id in wanted.Keys.Where(id => !producers.Any(b => b.id == id)).ToList())
            {
                wanted.Remove(id);
                waitSince.Remove(id);
            }
            var idle = producers.Where(b => b.queue.Count == 0).ToList();
            var optionsOf = new Dictionary<int, List<string>>();
            foreach (var b in idle)
            {
                var options = b.trains.Where(k => GameData.Unit(k) != null && f.tech >= GameData.I(GameData.Unit(k), "tech", 1)).ToList();
                optionsOf[b.id] = options;
                if (options.Count == 0 || wanted.ContainsKey(b.id)) continue;
                var support = options.Where(k => System.Array.IndexOf(Support, k) >= 0).ToList();
                var fighters = options.Where(k => System.Array.IndexOf(Support, k) < 0).ToList();
                if (fighters.Count == 0) fighters = options;
                var picks = mix[top] / (float)total > 0.4f ? CounterPicks[top].Where(fighters.Contains).ToList() : new List<string>();
                // healers and shield drones join once there is an army worth supporting
                wanted[b.id] = support.Count > 0 && army.Count > 8 && Random.value < 0.25f
                    ? support[Random.Range(0, support.Count)]
                    : picks.Count > 0 && Random.value < 0.7f
                        ? picks[Random.Range(0, picks.Count)]
                        : fighters[Random.Range(0, fighters.Count)];
                waitSince[b.id] = battle.time + Random.value * 0.5f;
            }

            float spare = f.res - reserve;
            foreach (var b in idle.Where(b => wanted.ContainsKey(b.id)).OrderBy(b => waitSince[b.id]))
            {
                string kind = wanted[b.id];
                int cost = Cost(kind);
                if (spare < cost && threat)
                {
                    // under attack: don't wait, take the best thing that can be afforded right now
                    kind = optionsOf[b.id].Where(k => Cost(k) <= spare).OrderByDescending(Cost).FirstOrDefault();
                    if (kind == null) continue;
                    cost = Cost(kind);
                }
                if (spare < cost)
                {
                    spare -= cost;        // hold the Biomass for it: those behind in line can't take it
                    continue;
                }
                if (battle.QueueUnit(b, kind) == null) spare -= cost;
                wanted.Remove(b.id);      // on failure (pop cap) it simply picks again next time
                waitSince.Remove(b.id);
            }
        }

        /// <summary>Evolutions: once there is Biomass to spare, an idle Evolution Chamber starts the next one.</summary>
        void Research(Faction f)
        {
            if (f.res < 250 + reserve) return;
            foreach (var b in battle.buildings)
            {
                if (!b.alive || b.faction != "alien" || !b.Done || b.queue.Count > 0) continue;
                var all = b.Research;
                if (all == null) continue;
                foreach (var kv in all)
                    if (!f.research.Contains(kv.Key) && !f.researching.Contains(kv.Key) && battle.QueueResearch(b, kv.Key) == null)
                        return;
            }
        }

        static int Cost(string unit) => GameData.I(GameData.Unit(unit), "cost", 100);
    }
}
