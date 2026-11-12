using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Outpost.Sim
{
    /// <summary>
    /// The alien faction's commander, ported from game/rts/ai.py. It plays by the same rules as the
    /// player (Biomass income, base slots, tech, pop cap) through the Battle's public methods;
    /// difficulty only changes its income, army strength and attack timing (rts.json "ai").
    ///
    /// Every second it: works through a list of purchases (build order, Spines, Deep Pools, Elder
    /// Hive) and sets part of its income aside for the next one while the rest keeps going into the
    /// army, keeps its production buildings busy with units that counter the player's army, defends
    /// its base and launches attack waves once the idle army is worth enough.
    /// </summary>
    public class AlienAI
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

        readonly Battle battle;
        readonly string[] buildOrder;
        readonly float attackEvery, armyMult;
        public float nextAttack;
        public int wave;
        /// <summary>Biomass set aside for the next purchase, and what that purchase is (for debugging).</summary>
        public float reserve;
        public string saving;
        float fund;                      // savings carried from one think to the next
        float thinkT = 2f;
        bool queenCalled;
        readonly HashSet<int> attackers = new HashSet<int>();

        public AlienAI(Battle b, string difficulty = "Normal")
        {
            battle = b;
            var ai = GameData.Rts["ai"];
            buildOrder = GameData.List(ai, "build_order");
            var d = ai["difficulties"]?[difficulty];
            battle.Alien.incomeMult = GameData.F(d, "income", 1f);
            armyMult = GameData.F(d, "army_mult", 1f);
            battle.alienArmyMult = armyMult;
            attackEvery = GameData.F(d, "attack_every", 120);
            nextAttack = GameData.F(d, "first_attack", 220);
        }

        Base Home => battle.bases.FirstOrDefault(b => b.faction == "alien" && b.Alive);

        /// <summary>The player base to hit: the closest, weakest-defended one.</summary>
        Base TargetBase(Base home)
        {
            Base best = null;
            float bs = float.MaxValue;
            foreach (var b in battle.bases)
            {
                if (!b.Alive || b.faction != "player") continue;
                float score = Vector2.Distance(new Vector2(b.x, b.y), new Vector2(home.x, home.y))
                              + b.sockets.Count(t => t != null) * 250;
                if (score < bs) { bs = score; best = b; }
            }
            return best;
        }

        public void OnUnit(Unit u) { }

        List<Unit> Army() => battle.units.Where(u => u.alive && u.faction == "alien" && u.lifetime < 0 &&
                                                     u.kind != "queen" && u.kind != "architect").ToList();

        public void Tick(float dt)
        {
            thinkT -= dt;
            if (thinkT > 0) return;
            thinkT = 1f;
            var home = Home;
            if (home == null) return;
            var f = battle.Alien;
            var army = Army();
            bool threat = battle.units.Any(u => u.alive && u.faction == "player" &&
                                                (u.Pos - new Vector2(home.x, home.y)).sqrMagnitude < 450 * 450);
            Economy(home, f, army, threat);
            Produce(f, army, threat);
            Research(f);
            Defend(home, army);
            ManageArmy(home, f, army);
            if (f.tech >= 3 && !queenCalled && f.hero == null)
            {
                queenCalled = true;
                f.heroRespawn = 1f;
            }
        }

        // ------------------------------------------------------------------ economy
        /// <summary>The next building the base wants from the build order (null when the plan is complete).</summary>
        string NextBuilding(Base home)
        {
            var have = home.slots.Take(home.SlotLimit).Where(s => s != null).Select(s => s.kind).ToList();
            var counts = new Dictionary<string, int>();
            foreach (var k in buildOrder)
            {
                counts.TryGetValue(k, out int n);
                counts[k] = ++n;
                if (have.Count(h => h == k) < n) return GameData.Building(k) != null ? k : null;
            }
            // spare slots after the plan: stealth breeds, then more income
            if (GameData.Building("den") != null && !have.Contains("den")) return "den";
            return "pool";
        }

        /// <summary>
        /// Buys the next thing on the list, or puts a share of income aside for it (more for things that
        /// raise income, since they pay for themselves). Saving only starts once there is a guard force,
        /// and the savings are spent on defense while the base is under attack.
        /// </summary>
        void Economy(Base home, Faction f, List<Unit> army, bool threat)
        {
            reserve = 0;
            saving = null;
            if (!home.hq.Done) return;
            int built = home.slots.Count(s => s != null);
            int spines = home.sockets.Count(t => t != null);
            int wantSpines = battle.time > 600 ? 4 : 2;
            bool spineOk = battle.time >= 150 && battle.FreeSocket(home) >= 0;
            string want = battle.FreeSlot(home) >= 0 ? NextBuilding(home) : null;
            if (want != null && f.tech < battle.BuildTech(want)) want = null;
            if (want == "node" && battle.buildings.Count(b => b.alive && b.faction == "alien" && b.kind == "node") >= 2) want = null;

            // the shopping list, in priority order: (name, cost, guard force needed before saving for it)
            var goals = new List<(string id, int cost, int guard)>();
            if (spineOk && spines == 0 && built >= 3) goals.Add(("spine", battle.TowerCost("spine"), 5));
            if (want != null) goals.Add((want, battle.BuildCost(want), Mathf.Min(2 + built, 8)));
            var pool = battle.buildings.FirstOrDefault(b => b.alive && b.faction == "alien" && b.kind == "pool" && b.Done &&
                                                            !b.upgraded && !b.Upgrading && b.Upgrade != null);
            if (pool != null && built >= 4) goals.Add(("pool_upgrade", GameData.I(pool.Upgrade, "cost", 220), 6));
            if (spineOk && spines < wantSpines) goals.Add(("spine", battle.TowerCost("spine"), 8));
            var hq = home.hq;
            if (!hq.upgraded && !hq.Upgrading && hq.Upgrade != null && f.tech >= GameData.I(hq.Upgrade, "tech", 2) && battle.time > 200)
                goals.Add(("hq_upgrade", GameData.I(hq.Upgrade, "cost", 600), 6));
            if (goals.Count == 0)
            {
                fund = 0;
                return;
            }

            var g = goals[0];
            if (f.res >= g.cost)
            {
                if (g.id == "spine") battle.BuildTower(home, "spine");
                else if (g.id == "hq_upgrade") battle.QueueUpgrade(hq);
                else if (g.id == "pool_upgrade") battle.QueueUpgrade(pool);
                else battle.Build(home, g.id);
                fund = 0;
                return;
            }
            if (threat || army.Count < g.guard) return;      // the fund stays, but everything is spendable
            bool pays = g.id == "pool" || g.id == "pool_upgrade";
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

        // ------------------------------------------------------------------ army
        void Defend(Base home, List<Unit> army)
        {
            float tx = 0, ty = 0;
            int n = 0;
            foreach (var u in battle.units)
                if (u.alive && u.faction == "player" && (u.Pos - new Vector2(home.x, home.y)).sqrMagnitude < 420 * 420)
                {
                    tx += u.x;
                    ty += u.y;
                    n++;
                }
            if (n == 0) return;
            var near = army.Where(u => !attackers.Contains(u.id) && (u.target == null || u.order != Order.Attack) &&
                                       (u.Pos - new Vector2(home.x, home.y)).sqrMagnitude < 1400 * 1400).ToList();
            if (near.Count > 0) battle.OrderMove(near, new Vector2(tx / n, ty / n), true);
        }

        void ManageArmy(Base home, Faction f, List<Unit> army)
        {
            attackers.RemoveWhere(id => !army.Any(u => u.id == id));
            var idle = army.Where(u => !attackers.Contains(u.id)).ToList();
            var target = TargetBase(home);
            var hp = new Vector2(home.x, home.y);
            var rally = target != null ? hp + (new Vector2(target.x, target.y) - hp).normalized * 300 : hp;

            // gather in front of the Hive, facing the player
            var strays = idle.Where(u => u.order == Order.None && u.target == null && (u.Pos - rally).sqrMagnitude > 260 * 260).ToList();
            if (strays.Count > 0) battle.OrderMove(strays, rally, false);

            // attack once the timer is up and the idle army is worth enough (or it has waited 90 s more)
            float value = idle.Sum(u => GameData.F(u.data, "cost"));
            float need = (500 + 250 * wave) * armyMult;
            if (battle.time >= nextAttack && (value >= need || battle.time >= nextAttack + 90) && target != null && idle.Count > 0)
            {
                int keep = idle.Count > 6 ? 2 : 0;       // a couple stay home as a guard
                var tp = new Vector2(target.x, target.y);
                var group = idle.OrderBy(u => (u.Pos - tp).sqrMagnitude).Take(idle.Count - keep).ToList();
                battle.OrderMove(group, tp, true);
                foreach (var u in group)
                {
                    attackers.Add(u.id);
                    if (u.kind == "burrowers" && battle.AbilityReady(u) && (tp - u.Pos).magnitude < 900) battle.UseAbility(u, tp);
                }
                wave++;
                nextAttack = battle.time + attackEvery;
                battle.fx?.Message("alien", $"Alien attack wave {wave} incoming!");
            }

            // attackers that went idle keep pushing
            if (target != null)
            {
                var lost = army.Where(u => attackers.Contains(u.id) && u.order == Order.None && u.target == null).ToList();
                if (lost.Count > 0) battle.OrderMove(lost, new Vector2(target.x, target.y), true);
            }

            // the Queen leads from the front when there is an attack
            var q = f.hero;
            if (q != null && q.alive && q.order == Order.None && q.target == null)
            {
                if (attackers.Count > 0 && target != null) battle.OrderMove(new[] { q }, new Vector2(target.x, target.y), true);
                else if ((q.Pos - rally).sqrMagnitude > 260 * 260) battle.OrderMove(new[] { q }, rally, false);
            }
        }
    }
}
