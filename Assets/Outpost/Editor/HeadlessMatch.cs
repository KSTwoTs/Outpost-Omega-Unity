using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using Outpost.Content;
using Outpost.Sim;

namespace Outpost.EditorTools
{
    /// <summary>
    /// Runs whole Skirmish matches without any graphics, in edit mode, with a scripted player against
    /// the alien AI, and reports what happened. Used to check every change to the battle rules the
    /// same way (Outpost > Run Headless Test Matches, or HeadlessMatch.Run(...) from code).
    ///
    /// Never tick a battle that has views attached in a long loop: effects are only recycled once per
    /// frame, so that fills the editor with objects. These battles have no view layer at all.
    /// </summary>
    public static class HeadlessMatch
    {
        /// <summary>
        /// player: "efficient" (plays the economy like the alien AI, expands, takes relays, defends, attacks
        /// with a big army and pulls back when beaten), "steady" (the same on one base: no expansions or relays),
        /// "casual" (slow, Marines only, never attacks) or "idle" (does nothing).
        /// </summary>
        public static string Run(string mapId = "dune_sea", string difficulty = "Normal", string player = "efficient",
            int seed = 1234, float maxTime = 1200f, float logEvery = 180f, string leader = "vanguard")
        {
            var sb = new StringBuilder();
            GameData.Reload();
            Random.InitState(seed);
            var map = MapBuilder.Load(mapId);
            var b = new Battle(map);
            b.SetLeader(leader);
            b.StandardStart(difficulty);
            var casts = new Dictionary<string, int>();
            var lastCd = new Dictionary<string, float>();
            var home = b.bases.FirstOrDefault(x => x.faction == "player");
            var hive = b.bases.FirstOrDefault(x => x.faction == "alien");
            if (home == null || hive == null) return $"{mapId}: no start sites";
            var hp = new Vector2(home.x, home.y);
            var ap = new Vector2(hive.x, hive.y);
            var dir = (ap - hp).normalized;

            sb.AppendLine($"== {mapId}, {difficulty}, {player} player, leader {b.leader}, seed {seed}");
            var sw = System.Diagnostics.Stopwatch.StartNew();
            string[] order = player == "efficient" ? new[] { "barracks", "supply", "reactor", "depot" }
                : player == "casual" ? new[] { "barracks" } : new string[0];
            int oi = 0, lastWave = 0, lastLog = -1, heroDeaths = 0, lastRaids = 0, lastScouts = 0;
            float next = 0;
            bool heroWas = true;
            var rally = hp + dir * 260;
            var brain = player == "efficient" ? new Efficient(b, home, sb)
                : player == "steady" ? new Efficient(b, home, sb) { oneBase = true } : null;
            while (b.winner == null && b.time < maxTime && sw.ElapsedMilliseconds < 20000)
            {
                b.Tick(0.05f);
                CountCasts(b, casts, lastCd);
                bool heroNow = b.Player.hero != null;
                if (heroWas && !heroNow) heroDeaths++;
                heroWas = heroNow;
                if (b.time < next) continue;
                next = b.time + (player == "efficient" ? 1f : 4f);
                var P = b.Player;
                var army = b.units.Where(u => u.alive && u.faction == "player").ToList();

                if (brain != null)
                {
                    brain.Think(1f);
                    foreach (var u in army)
                        if (u.target != null && b.AbilityReady(u)) b.UseAbility(u, u.target.Pos);
                    UsePowers(b, home, hive, brain.attacking);
                }
                else if (player == "casual")
                {
                    if (oi < order.Length && P.res >= b.BuildCost(order[oi]) && b.FreeSlot(home) >= 0 && b.Build(home, order[oi]) == null) oi++;
                    if (oi >= 2 && b.time > 120 && P.res > 400 && b.FreeSocket(home) >= 0 && home.sockets.Count(s => s != null) < 2)
                        b.BuildTower(home, "pulse");
                    var idle = b.buildings.Where(x => x.alive && x.faction == "player" && x.Done && x.queue.Count == 0).ToList();
                    foreach (var bl in idle)
                    {
                        if (bl.trains.Length > 0) bl.rally = rally;
                        if (bl.trains.Length > 0 && P.res > 250) b.QueueUnit(bl, "marines");
                    }
                }

                if (b.ai.wave != lastWave)
                {
                    lastWave = b.ai.wave;
                    sb.AppendLine($"  wave {lastWave} at {(int)b.time}s: {Mix(b, "alien")} vs player {army.Count}");
                }
                if (b.ai.scoutsSent != lastScouts)
                {
                    lastScouts = b.ai.scoutsSent;
                    sb.AppendLine($"  AI scout sent at {(int)b.time}s");
                }
                if (b.ai.raidsSent != lastRaids)
                {
                    lastRaids = b.ai.raidsSent;
                    sb.AppendLine($"  AI raid {lastRaids} at {(int)b.time}s, player explored {b.FogOf("player").ExploredShare():P0}");
                }
                int slot = (int)(b.time / logEvery);
                if (slot != lastLog)
                {
                    lastLog = slot;
                    var A = b.Alien;
                    sb.AppendLine($"{(int)b.time}s ALIEN res {(int)A.res} inc {A.income:0.0} tech {A.tech} pop {A.popUsed}/{A.popCap} saving={b.ai.saving}" +
                                  $" bld[{string.Join(",", hive.slots.Where(s => s != null).Select(s => s.kind + (s.upgraded ? "+" : "") + (s.Done ? "" : "*")))}]" +
                                  $" spines {hive.sockets.Count(s => s != null)} elder={hive.hq.upgraded} bases {b.bases.Count(x => x.Alive && x.faction == "alien")}" +
                                  $" relays {b.relays.Count(r => r.owner == "alien")}/{b.relays.Count(r => r.owner == "player")} hero={(A.hero != null ? A.hero.kind : "-")} research[{string.Join(",", A.research)}]" +
                                  $" | PLAYER res {(int)P.res} inc {P.income:0.0} tech {P.tech} units {army.Count} hq {(int)home.hq.hp}" +
                                  $" bld[{string.Join(",", home.slots.Where(s => s != null).Select(s => s.kind + (s.Done ? "" : "*")))}] research[{string.Join(",", P.research)}]" +
                                  $" towers[{TowerList(b, "player")}]");
                }
            }
            sb.AppendLine($"end t={(int)b.time} winner={b.winner ?? "none"} ({sw.ElapsedMilliseconds} ms) kills player/alien {b.Player.kills}/{b.Alien.kills}" +
                          $" commander deaths {heroDeaths} hq {(int)home.hq.hp} hive {(int)hive.hq.hp}");
            sb.AppendLine($"  final armies: player {Mix(b, "player")} | alien {Mix(b, "alien")} | wild {Mix(b, "wild")}");
            sb.AppendLine($"  towers: player [{TowerList(b, "player")}] alien [{TowerList(b, "alien")}]");
            sb.AppendLine($"  vision: explored player {b.FogOf("player").ExploredShare():P0} / alien {b.FogOf("alien").ExploredShare():P0}; fog {b.fogUpdates} updates, {b.fogMsTotal / Mathf.Max(1, b.fogUpdates):0.000} ms each; AI scouts {b.ai.scoutsSent}, raids {b.ai.raidsSent}");
            sb.AppendLine($"  alerts {b.alerts.Count}: {string.Join(", ", b.alerts.GroupBy(a => a.text.Split('!', ':')[0]).Select(g => g.Key + " x" + g.Count()))}");
            sb.AppendLine($"  powers cast: {(casts.Count == 0 ? "none" : string.Join(" ", casts.OrderBy(kv => kv.Key).Select(kv => kv.Key + "x" + kv.Value)))}");
            sb.AppendLine($"  bases player/alien {b.bases.Count(x => x.Alive && x.faction == "player")}/{b.bases.Count(x => x.Alive && x.faction == "alien")}" +
                          $", crates left {b.crates.Count(c => c.alive)}/{b.crates.Count}, relays player/alien {b.relays.Count(r => r.owner == "player")}/{b.relays.Count(r => r.owner == "alien")}");
            return sb.ToString();
        }

        // ------------------------------------------------------------------ powers
        /// <summary>Counts every power cast by either side (its cooldown jumps up).</summary>
        static void CountCasts(Battle b, Dictionary<string, int> casts, Dictionary<string, float> last)
        {
            foreach (var f in new[] { b.Player, b.Alien })
                foreach (var kv in f.powerCd)
                {
                    string key = (f.name == "player" ? "P:" : "A:") + kv.Key;
                    float before;
                    if (last.TryGetValue(key, out before) && kv.Value > before + 0.5f)
                        casts[key] = (casts.TryGetValue(key, out var n) ? n : 0) + 1;
                    last[key] = kv.Value;
                }
        }

        /// <summary>The biggest clump of alien units (count, centre) within reach of a spot, or count 0.</summary>
        static (int n, Vector2 at) AlienClump(Battle b, Vector2 near, float reach, float radius = 150f)
        {
            var foes = b.units.Where(u => u.alive && u.faction == "alien" && (u.Pos - near).sqrMagnitude < reach * reach).ToList();
            (int n, Vector2 at) best = (0, near);
            foreach (var u in foes)
            {
                var group = foes.Where(o => (o.Pos - u.Pos).sqrMagnitude < radius * radius).ToList();
                if (group.Count <= best.n) continue;
                var c = Vector2.zero;
                foreach (var o in group) c += o.Pos;
                best = (group.Count, c / group.Count);
            }
            return best;
        }

        /// <summary>
        /// The efficient player's leader powers: Orbital Strike and the leader's attack power on big alien clumps
        /// (5+ near its bases or its army), Cryo Bomb on 4+ attacking a base, Repair Wave where its own things are
        /// most hurt, Bulwark when a base is under attack, Supply Drop whenever it is ready, Turret Drop to defend.
        /// It keeps 150 Supplies back for units unless the fight is at one of its bases.
        /// </summary>
        static void UsePowers(Battle b, Base home, Base hive, bool attacking)
        {
            var P = b.Player;
            var mine = b.bases.Where(x => x.Alive && x.faction == "player").ToList();
            var army = b.units.Where(u => u.alive && u.faction == "player" && u.pop > 0).ToList();
            (int n, Vector2 at) defend = (0, Vector2.zero), field = (0, Vector2.zero);
            foreach (var bs in mine)
            {
                var c = AlienClump(b, new Vector2(bs.x, bs.y), 650);
                if (c.n > defend.n) defend = c;
            }
            if (army.Count > 0)
            {
                var centre = Vector2.zero;
                foreach (var u in army) centre += u.Pos;
                centre /= army.Count;
                field = AlienClump(b, centre, 600);
            }
            var fight = defend.n >= field.n ? defend : field;
            bool atHome = defend.n > 0 && defend.n >= field.n;
            System.Func<string, bool> afford = pid => b.CanPower(pid) == null && (atHome || P.res >= b.PowerCost(pid) + 150);
            string v = b.playerPowers[3];
            if (v == "supply_drop" && b.CanPower(v) == null) b.CastPower(v, new Vector2(home.x, home.y));
            if (fight.n >= 5 && afford("orbital")) b.CastPower("orbital", fight.at);
            if (defend.n >= 4 && afford("cryo")) b.CastPower("cryo", defend.at);
            bool vAttack = v == "mech" || v == "carpet" || v == "firestorm" || v == "deep_freeze" || v == "sniper_net" || v == "drone_swarm";
            if (vAttack && fight.n >= 5 && afford(v)) b.CastPower(v, fight.at);
            if (v == "turret_drop" && defend.n >= 4 && afford(v)) b.CastPower(v, defend.at);
            if (v == "bulwark" && defend.n >= 6 && afford(v)) b.CastPower(v, new Vector2(home.x, home.y));
            // Repair Wave where the most of its own hit points are missing (at least 1200)
            if (afford("repair"))
            {
                var own = b.All().Where(e => e.alive && e.faction == "player" && e.Done).ToList();
                float bestMiss = 1200f;
                Vector2? spot = null;
                foreach (var e in own)
                {
                    if (e.hp > e.maxHp * 0.8f) continue;
                    float miss = own.Where(o => (o.Pos - e.Pos).sqrMagnitude < 200f * 200f).Sum(o => o.maxHp - o.hp);
                    if (miss > bestMiss) { bestMiss = miss; spot = e.Pos; }
                }
                if (spot.HasValue) b.CastPower("repair", spot.Value);
            }
        }

        // ------------------------------------------------------------------ the efficient player
        /// <summary>
        /// The "efficient" scripted player: plays the economy the way the alien AI does (savings fund for
        /// buildings, Supply Pad upgrades, expansions on a timer, Fortress), keeps every production building
        /// busy, takes nearby relays, defends its bases with the whole army, attacks when its army is big
        /// and pulls back when the attack has been beaten. Meant to be a fair benchmark for balance checks.
        /// </summary>
        class Efficient
        {
            readonly Battle b;
            readonly Base home;
            readonly StringBuilder log;
            readonly Vector2 hp;
            float fund;
            public bool attacking;
            int attackSize;
            readonly Dictionary<Relay, List<Unit>> relayTeams = new Dictionary<Relay, List<Unit>>();
            readonly List<Unit> escort = new List<Unit>();
            string wantBar;
            /// <summary>"steady" player: the same play on one base only (no expansions, no relays).</summary>
            public bool oneBase;
            static readonly string[] HomeOrder = { "barracks", "supply", "reactor", "depot", "supply", "reactor", "barracks" };
            static readonly string[] ExpOrder = { "supply", "supply", "barracks", "supply", "supply" };

            public Efficient(Battle b, Base home, StringBuilder log)
            {
                this.b = b;
                this.home = home;
                this.log = log;
                hp = new Vector2(home.x, home.y);
            }

            List<Base> Mine => b.bases.Where(x => x.Alive && x.faction == "player").ToList();

            static int Count(Base bs, string kind) => bs.slots.Count(s => s != null && s.alive && s.kind == kind);

            /// <summary>The next economy purchase in priority order: (what, cost, buy). buy returns an error or null.</summary>
            (string what, int cost, System.Func<string> buy) NextGoal(List<Base> mine)
            {
                var P = b.Player;
                // 1. the main base's buildings (the last two slots only exist after the Fortress upgrade)
                if (b.FreeSlot(home) >= 0)
                {
                    var have = new Dictionary<string, int>();
                    foreach (var k in HomeOrder)
                    {
                        have[k] = have.TryGetValue(k, out var n) ? n + 1 : 1;
                        if (Count(home, k) < have[k])
                        {
                            string kind = k;
                            return ("home " + kind, b.BuildCost(kind, "player"), () => b.Build(home, kind));
                        }
                    }
                }
                // 2. an expansion when one is due (same timing rule as the alien AI on this difficulty)
                var site = ExpansionSite(mine);
                if (site != null)
                    return ("expand", b.ExpansionCost("player"), () => TryExpand(site));
                // 3. Supply Pad upgrades
                var pad = b.buildings.FirstOrDefault(x => x.alive && x.faction == "player" && x.kind == "supply" && x.Done && !x.upgraded && !x.Upgrading && x.queue.Count == 0);
                if (pad != null) return ("upgrade pad", GameData.I(pad.Upgrade, "cost", 225), () => b.QueueUpgrade(pad));
                // 4. buildings at the expansions
                foreach (var bs in mine)
                {
                    if (bs == home || !bs.hq.Done || b.FreeSlot(bs) < 0) continue;
                    var have = new Dictionary<string, int>();
                    foreach (var k in ExpOrder)
                    {
                        have[k] = have.TryGetValue(k, out var n) ? n + 1 : 1;
                        if (Count(bs, k) < have[k])
                        {
                            string kind = k;
                            var at = bs;
                            return ("build " + kind, b.BuildCost(kind, "player"), () => b.Build(at, kind));
                        }
                    }
                }
                // 5. the Fortress (two more slots at the main base)
                if (P.tech >= 2 && b.time > 360 && !home.hq.upgraded && !home.hq.Upgrading && home.hq.queue.Count == 0)
                    return ("fortress", GameData.I(home.hq.Upgrade, "cost", 650), () => b.QueueUpgrade(home.hq));
                // 6. one Pulse Turret per base
                if (b.time > 300)
                    foreach (var bs in mine)
                        if (bs.hq.Done && bs.sockets.All(s => s == null) && b.FreeSocket(bs) >= 0)
                        {
                            var at = bs;
                            return ("pulse", (int)b.TowerCost("pulse"), () => b.BuildTower(at, "pulse"));
                        }
                return (null, 0, null);
            }

            MapData.SiteData ExpansionSite(List<Base> mine)
            {
                float t = b.time;
                if (oneBase) return null;
                float expandAt = GameData.F(GameData.Rts["ai"]["difficulties"]["Normal"], "expand_at", 270);
                if (mine.Count >= 4 || t < expandAt + 240 * (mine.Count - 1)) return null;
                return b.FreeSites().Where(x => !b.wildSites.Contains(x.id))
                    .OrderBy(x => (new Vector2(x.x, x.y) - hp).sqrMagnitude).FirstOrDefault();
            }

            /// <summary>Walks an escort to the site and founds the base once they are there.</summary>
            string TryExpand(MapData.SiteData site)
            {
                var at = new Vector2(site.x, site.y);
                string err = b.CanExpand("player", site);
                if (err == "Need units nearby")
                {
                    escort.RemoveAll(u => !u.alive);
                    if (escort.Count < 3)
                        escort.AddRange(b.units.Where(u => u.alive && u.faction == "player" && u.pop > 0 && !escort.Contains(u) && !InRelayTeam(u))
                            .OrderBy(u => (u.Pos - at).sqrMagnitude).Take(3 - escort.Count));
                    b.OrderMove(escort.Where(u => (u.Pos - at).sqrMagnitude > 200 * 200), at, true);
                    return err;
                }
                err = b.Expand("player", site);
                if (err == null)
                {
                    log.AppendLine($"  player expands at {(int)b.time}s (base {Mine.Count})");
                    escort.Clear();
                }
                return err;
            }

            bool InRelayTeam(Unit u) => relayTeams.Values.Any(l => l.Contains(u));

            public void Think(float dt)
            {
                var P = b.Player;
                var mine = Mine;
                fund = Mathf.Min(fund + P.income * dt * 0.45f, 900f);
                var goal = NextGoal(mine);
                if (goal.buy == null) fund = 0;
                else if (P.res >= goal.cost)
                {
                    if (goal.buy() == null) fund = Mathf.Max(0, fund - goal.cost);
                }
                float keep = goal.buy == null ? 0 : Mathf.Min(fund, goal.cost);
                Produce(keep);
                Research(keep);
                Army(mine);
            }

            void Produce(float keep)
            {
                var P = b.Player;
                var army = b.units.Where(u => u.alive && u.faction == "player" && u.pop > 0).ToList();
                foreach (var bl in b.buildings)
                {
                    if (!bl.alive || bl.faction != "player" || !bl.Done || bl.trains.Length == 0 || bl.queue.Count > 0 || bl.Upgrading) continue;
                    string kind;
                    if (bl.kind == "barracks")
                    {
                        if (wantBar == null)
                        {
                            float r = Random.value;
                            wantBar = P.tech >= 2 && r < 0.35f ? "rockets" : P.tech >= 2 && r < 0.5f ? "flamers"
                                : army.Count > 6 && r < 0.6f ? "medics" : "marines";
                        }
                        kind = wantBar;
                    }
                    else if (bl.kind == "depot") kind = P.tech >= 2 ? "tank" : "buggy";
                    else continue;
                    int cost = b.UnitCost("player", kind);
                    if (P.res - cost < keep) continue;
                    if (b.QueueUnit(bl, kind) == null && bl.kind == "barracks") wantBar = null;
                }
            }

            void Research(float keep)
            {
                var P = b.Player;
                if (P.res < keep + 350 || P.researching.Count > 0) return;
                foreach (var bl in b.buildings)
                {
                    if (!bl.alive || bl.faction != "player" || !bl.Done || bl.Research == null || bl.queue.Count > 0) continue;
                    foreach (var kv in bl.Research)
                    {
                        if (P.research.Contains(kv.Key) || P.researching.Contains(kv.Key)) continue;
                        if (P.tech < GameData.I(kv.Value, "tech", 1)) continue;
                        string after = GameData.S(kv.Value, "after");
                        if (after != null && !P.research.Contains(after)) continue;
                        if (b.QueueResearch(bl, kv.Key) == null) return;
                    }
                }
            }

            static Vector2 Centre(IEnumerable<Unit> us)
            {
                var c = Vector2.zero;
                int n = 0;
                foreach (var u in us) { c += u.Pos; n++; }
                return n == 0 ? c : c / n;
            }

            void Army(List<Base> mine)
            {
                var P = b.Player;
                var foes = b.units.Where(u => u.alive && u.faction == "alien").ToList();
                var army = b.units.Where(u => u.alive && u.faction == "player" && u.pop > 0).ToList();
                var cmd = b.units.FirstOrDefault(u => u.alive && u.faction == "player" && u.pop == 0 && u.kind == "commander");

                // relay teams: two units take each unowned relay near our bases while nothing guards it
                foreach (var kv in relayTeams.ToList())
                {
                    kv.Value.RemoveAll(u => !u.alive);
                    if (kv.Key.owner == "player" || kv.Value.Count == 0) relayTeams.Remove(kv.Key);
                }
                if (!oneBase && army.Count >= 6 && relayTeams.Count < 2)
                    foreach (var r in b.relays)
                    {
                        if (r.owner == "player" || relayTeams.ContainsKey(r) || relayTeams.Count >= 2) continue;
                        var rp = new Vector2(r.x, r.y);
                        if (!mine.Any(bs => (new Vector2(bs.x, bs.y) - rp).sqrMagnitude < 1300f * 1300f)) continue;
                        if (b.units.Any(u => u.alive && (u.faction == "alien" || u.faction == "wild") && (u.Pos - rp).sqrMagnitude < 450f * 450f)) continue;
                        var team = army.Where(u => !InRelayTeam(u) && !escort.Contains(u)).OrderBy(u => (u.Pos - rp).sqrMagnitude).Take(2).ToList();
                        if (team.Count < 2) break;
                        relayTeams[r] = team;
                        b.OrderMove(team, rp, true);
                    }
                var free = army.Where(u => !InRelayTeam(u) && !escort.Contains(u)).ToList();
                if (cmd != null) free.Add(cmd);

                // defend: the whole army goes to the base with the most aliens near it
                Base threatened = null;
                int worst = 1;
                foreach (var bs in mine)
                {
                    var bp = new Vector2(bs.x, bs.y);
                    int n = foes.Count(u => (u.Pos - bp).sqrMagnitude < 600f * 600f);
                    if (n > worst) { worst = n; threatened = bs; }
                }
                if (threatened != null)
                {
                    var bp = new Vector2(threatened.x, threatened.y);
                    var near = foes.Where(u => (u.Pos - bp).sqrMagnitude < 600f * 600f);
                    var c = Centre(near);
                    b.OrderMove(free.Where(u => u.target == null || (u.Pos - c).sqrMagnitude > 500f * 500f), c, true);
                    if (attacking) { attacking = false; log.AppendLine($"  player recalls its army to defend at {(int)b.time}s"); }
                    return;
                }

                int popArmy = army.Sum(u => u.pop);
                if (attacking)
                {
                    if (army.Count < attackSize * 0.4f)
                    {
                        attacking = false;
                        log.AppendLine($"  player pulls back at {(int)b.time}s with {army.Count}");
                        b.OrderMove(free, Rally(mine), false);
                        return;
                    }
                    var target = Target();
                    if (target == null) return;
                    var tp = new Vector2(target.x, target.y);
                    b.OrderMove(free.Where(u => u.order == Order.None && u.target == null), tp, true);
                    return;
                }
                if (b.time > 360 && popArmy >= Mathf.Min(P.popCap - 4, 34))
                {
                    attacking = true;
                    attackSize = army.Count;
                    var target = Target();
                    log.AppendLine($"  player attacks at {(int)b.time}s with {army.Count} units (pop {popArmy}) -> {(target == home ? "?" : target?.hq.kind + " base")}");
                    if (target != null) b.OrderMove(free, new Vector2(target.x, target.y), true);
                    return;
                }
                // gather at the rally point
                var rally = Rally(mine);
                b.OrderMove(free.Where(u => u.order == Order.None && u.target == null && (u.Pos - rally).sqrMagnitude > 250f * 250f), rally, true);
            }

            /// <summary>The alien base to attack: the closest one to our main base.</summary>
            Base Target() => b.bases.Where(x => x.Alive && x.faction == "alien")
                .OrderBy(x => (new Vector2(x.x, x.y) - hp).sqrMagnitude).FirstOrDefault();

            /// <summary>In front of the newest base, toward the nearest alien base.</summary>
            Vector2 Rally(List<Base> mine)
            {
                var t = Target();
                var from = mine.Count > 0 ? mine[mine.Count - 1] : home;
                var fp = new Vector2(from.x, from.y);
                if (t == null) return fp;
                return fp + (new Vector2(t.x, t.y) - fp).normalized * 260f;
            }
        }

        static string TowerList(Battle b, string faction) =>
            string.Join(",", b.towers.Where(t => t.alive && t.faction == faction).Select(t => t.kind + (t.Done ? "" : "*") + (t.tier > 0 ? "+" + t.tier : "")));

        static string Mix(Battle b, string faction) =>
            string.Join(" ", b.units.Where(u => u.alive && u.faction == faction).GroupBy(u => u.kind).Select(g => g.Key + "x" + g.Count()));

        /// <summary>The standard set: efficient and casual players on Normal, efficient on Hard (full logs).</summary>
        public static string RunSuite(string mapId = "dune_sea")
        {
            return Run(mapId, "Normal", "efficient", 1234) + "\n" + Run(mapId, "Normal", "casual", 1235) + "\n" + Run(mapId, "Hard", "efficient", 1236);
        }

        /// <summary>
        /// The balance check (roadmap step 9): every map x difficulty x player, several seeds each, one line per
        /// cell with the player's wins and the match lengths (p = player won, a = alien won, - = no winner).
        /// </summary>
        public static string BalanceTable(int seeds = 3, string leader = "vanguard", string players = "efficient,steady,casual",
            string maps = "dune_sea,hive_reach,magma_rift,frostbite", string difficulties = "Easy,Normal,Hard,Legendary")
        {
            var sb = new StringBuilder();
            sb.AppendLine($"balance table, leader {leader}, {seeds} seeds per cell, matches up to 30 min");
            foreach (var p in players.Split(','))
                foreach (var m in maps.Split(','))
                {
                    sb.Append($"{p,-9} {m,-11}");
                    foreach (var d in difficulties.Split(','))
                    {
                        int wins = 0;
                        var times = new List<string>();
                        for (int i = 1; i <= seeds; i++)
                        {
                            string r = Run(m, d, p, i * 101, 1800f, 1800f, leader);
                            var end = r.Split('\n').FirstOrDefault(l => l.StartsWith("end ")) ?? "";
                            var w = System.Text.RegularExpressions.Regex.Match(end, "t=(\\d+) winner=(\\w+)");
                            if (w.Groups[2].Value == "player") wins++;
                            string who = w.Groups[2].Value == "player" ? "p" : w.Groups[2].Value == "alien" ? "a" : "-";
                            times.Add(who + w.Groups[1].Value);
                        }
                        sb.Append($" | {d} {wins}/{seeds} {string.Join(" ", times)}");
                    }
                    sb.AppendLine();
                }
            return sb.ToString();
        }

        [MenuItem("Outpost/Run Headless Test Matches", priority = 40)]
        static void Menu()
        {
            if (EditorApplication.isPlaying)
            {
                Debug.LogWarning("[Outpost] Stop play mode before running headless matches.");
                return;
            }
            Debug.Log("[Outpost] Headless test matches\n" + RunSuite());
        }
    }
}
