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
        /// player: "efficient" (good build order, mixed army, research, attacks at 7 minutes),
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
            int buys = 0;
            string wantBar = null;
            float next = 0;
            bool attacked = false, heroWas = true;
            var rally = hp + dir * 260;
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
                MapData.SiteData expandSite = null;

                if (player != "idle")
                {
                    if (oi < order.Length && P.res >= b.BuildCost(order[oi]) && b.FreeSlot(home) >= 0 && b.Build(home, order[oi]) == null) oi++;
                    if (player == "efficient") Towers(b, home, oi >= 2);
                    else if (oi >= 2 && b.time > 120 && P.res > 400 && b.FreeSocket(home) >= 0 && home.sockets.Count(s => s != null) < 2)
                        b.BuildTower(home, "pulse");
                    // the efficient player holds back on units while it saves for its next building
                    bool saving = player == "efficient" && oi < order.Length && army.Count >= 3 + oi;
                    if (player == "efficient" && oi >= order.Length)
                    {
                        // a good player expands: up to three bases, each filled with Supply Pads first
                        var mine = b.bases.Where(x => x.Alive && x.faction == "player").ToList();
                        foreach (var bs in mine)
                        {
                            if (bs == home || !bs.hq.Done || bs.slots.Count(x => x != null && x.kind == "supply") >= 3 || b.FreeSlot(bs) < 0) continue;
                            if (b.Build(bs, "supply") != null && army.Count >= 6) saving = true;
                            break;
                        }
                        expandSite = b.time > 200 && mine.Count < 3
                            ? b.FreeSites().Where(x => !b.wildSites.Contains(x.id)).OrderBy(x => (new Vector2(x.x, x.y) - hp).sqrMagnitude).FirstOrDefault()
                            : null;
                        if (expandSite != null && army.Count >= 6) saving = true;
                    }
                    var idle = b.buildings.Where(x => x.alive && x.faction == "player" && x.Done && x.queue.Count == 0).ToList();
                    foreach (var bl in idle) if (bl.trains.Length > 0) bl.rally = rally;
                    if (player == "casual")
                    {
                        foreach (var bl in idle)
                            if (bl.trains.Length > 0 && P.res > 250) b.QueueUnit(bl, "marines");
                    }
                    else if (!saving)
                    {
                        // purchases take turns so cheap Marines don't starve everything else:
                        // barracks, barracks, depot, barracks, barracks, research
                        int turn = buys % 6;
                        var bar = idle.FirstOrDefault(x => x.kind == "barracks");
                        var dep = b.buildings.FirstOrDefault(x => x.alive && x.faction == "player" && x.kind == "depot" && x.Done);
                        string err = null;
                        if (turn == 5)
                        {
                            Building lab = null;
                            string rid = null;
                            foreach (var bl in b.buildings)
                            {
                                if (!bl.alive || bl.faction != "player" || !bl.Done || bl.Research == null || bl.queue.Count >= 5) continue;
                                foreach (var kv in bl.Research)
                                {
                                    if (P.research.Contains(kv.Key) || P.researching.Contains(kv.Key)) continue;
                                    if (P.tech < GameData.I(kv.Value, "tech", 1)) continue;
                                    string after = GameData.S(kv.Value, "after");
                                    if (after != null && !P.research.Contains(after)) continue;
                                    lab = bl;
                                    rid = kv.Key;
                                    break;
                                }
                                if (lab != null) break;
                            }
                            if (lab == null || oi < order.Length) buys++;
                            else if (b.QueueResearch(lab, rid) == null) buys++;
                        }
                        else if (turn == 2)
                        {
                            if (dep == null) buys++;
                            else if (dep.queue.Count == 0)
                            {
                                err = b.QueueUnit(dep, P.tech >= 2 ? "tank" : "buggy");
                                if (err == null || err.StartsWith("Population")) buys++;
                            }
                        }
                        else if (bar != null)
                        {
                            if (wantBar == null)
                                wantBar = army.Count > 8 && Random.value < 0.15f ? "medics" : P.tech >= 2 && Random.value < 0.5f ? "rockets" : "marines";
                            err = b.QueueUnit(bar, wantBar);
                            if (err == null || err.StartsWith("Population"))
                            {
                                buys++;
                                wantBar = null;
                            }
                        }
                    }
                }
                if (expandSite != null)
                {
                    var at = new Vector2(expandSite.x, expandSite.y);
                    var party = army.Where(u => u.pop > 0).OrderBy(u => (u.Pos - at).sqrMagnitude).Take(3).ToList();
                    if (b.Expand("player", expandSite) == null) sb.AppendLine($"  player expands at {(int)b.time}s");
                    else if (party.Count > 0 && (party[0].Pos - at).sqrMagnitude > 300 * 300 && party[0].order == Order.None)
                        b.OrderMove(party, at, true);
                }
                if (player == "efficient")
                {
                    if (b.time > 420 && army.Count >= 14 && !attacked)
                    {
                        attacked = true;
                        sb.AppendLine($"  player attacks at {(int)b.time}s with {army.Count}");
                    }
                    if (attacked) b.OrderMove(army.Where(u => u.order == Order.None && u.target == null), ap, true);
                    foreach (var u in army)
                        if (u.target != null && b.AbilityReady(u)) b.UseAbility(u, u.target.Pos);
                    UsePowers(b, home, hive, attacked);
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

        // the efficient player's turrets: a starter set at the main base, one Pulse Turret at each expansion,
        // then tier upgrades once it has spare Supplies (exercises build / upgrade / behaviors in the suite)
        static readonly string[] HomeTowers = { "pulse", "tesla", "cryo", "radar" };

        static void Towers(Battle b, Base home, bool started)
        {
            var P = b.Player;
            if (!started || b.time < 120) return;
            int have = home.sockets.Count(s => s != null);
            if (have < HomeTowers.Length && P.res > 400 && b.FreeSocket(home) >= 0)
            {
                b.BuildTower(home, HomeTowers[have]);
                return;
            }
            foreach (var bs in b.bases)
                if (bs != home && bs.Alive && bs.faction == "player" && bs.hq.Done && bs.sockets.All(s => s == null) && P.res > 400)
                {
                    b.BuildTower(bs, "pulse");
                    return;
                }
            if (b.time < 300 || P.res < 500) return;
            var up = b.towers.Where(t => t.alive && t.faction == "player" && t.Done && t.UpgradeCost() >= 0)
                .OrderBy(t => t.tier).ThenBy(t => t.UpgradeCost()).FirstOrDefault();
            if (up != null) b.UpgradeTower(up);
        }

        static string TowerList(Battle b, string faction) =>
            string.Join(",", b.towers.Where(t => t.alive && t.faction == faction).Select(t => t.kind + (t.Done ? "" : "*") + (t.tier > 0 ? "+" + t.tier : "")));

        static string Mix(Battle b, string faction) =>
            string.Join(" ", b.units.Where(u => u.alive && u.faction == faction).GroupBy(u => u.kind).Select(g => g.Key + "x" + g.Count()));

        /// <summary>The standard set: efficient and casual players on Normal, efficient on Hard.</summary>
        public static string RunSuite(string mapId = "dune_sea")
        {
            return Run(mapId, "Normal", "efficient", 1234) + "\n" + Run(mapId, "Normal", "casual", 1235) + "\n" + Run(mapId, "Hard", "efficient", 1236);
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
