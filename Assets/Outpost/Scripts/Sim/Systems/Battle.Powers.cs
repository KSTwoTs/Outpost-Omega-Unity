// Battle.Powers.cs · roadmap step M1.6 (filled Nov 20).
// Leader powers (Z X C V) and alien powers. Port of sim.py power_cost / power_cooldown / can_power / cast_power /
// alien_power; numbers from rts.json "powers", "leader_powers" and "alien_powers".

using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Outpost.Sim
{
    /// <summary>
    /// Leader powers: Z Orbital Strike, X Cryo Bomb, C Repair Wave for everyone, V the leader's own (Mech Drop,
    /// Turret Drop, Carpet Bombing, Firestorm, Deep Freeze, Bulwark, Drone Swarm, Supply Drop, Sniper Network).
    /// Each costs Supplies and then recharges; the cooldown gets shorter with the leader (tactician), Capacitors
    /// research, captured relays (8% each) and Uplink towers (Faction.powerCut), never below 40%. Every power is
    /// aimed at a spot on the map. The Swarm has Spore Storm, Acid Rain and Swarm Call (free, cooldown only),
    /// cast by the AI (AlienAI.Powers). Cooldowns tick in Economy. The view hears about each one via IBattleFx.Power.
    /// </summary>
    public partial class Battle
    {
        /// <summary>Every player power by id (Z X C shared and the leader's V power), as rts.json entries plus "id".</summary>
        public readonly Dictionary<string, JObject> powers = new Dictionary<string, JObject>();
        /// <summary>The player's four power ids in key order Z X C V.</summary>
        public string[] playerPowers = new string[0];
        public static readonly string[] PowerKeys = { "Z", "X", "C", "V" };

        /// <summary>The Python Kestrel drone (sim.py SWARM_DRONE).</summary>
        const float DroneHp = 90, DroneSpeed = 165, DroneRange = 140, DroneDamage = 7, DroneRate = 2.5f, DroneSight = 220;

        JObject AlienPowers => GameData.Rts["alien_powers"] as JObject;

        void SetupPowers()
        {
            powers.Clear();
            if (GameData.Rts["powers"] is JObject all)
                foreach (var p in all.Properties())
                    if (p.Name != "mech" && p.Value is JObject o)
                    {
                        var c = (JObject)o.DeepClone();
                        c["id"] = p.Name;
                        powers[p.Name] = c;
                    }
            var lp = (GameData.Rts["leader_powers"]?[leader] ?? GameData.Rts["powers"]?["mech"]) as JObject;
            lp = lp != null ? (JObject)lp.DeepClone() : new JObject { ["name"] = "Mech Drop", ["cost"] = 450, ["cooldown"] = 140 };
            if (lp["id"] == null) lp["id"] = "mech";
            if (lp["key"] == null) lp["key"] = "V";
            string vid = (string)lp["id"];
            powers[vid] = lp;
            playerPowers = new[] { "orbital", "cryo", "repair", vid };
            var P = Player;
            P.powerCd.Clear();
            foreach (var pid in playerPowers) P.powerCd[pid] = 0f;
            var A = Alien;
            A.powerCd.Clear();
            if (AlienPowers != null)
                foreach (var p in AlienPowers.Properties()) A.powerCd[p.Name] = 30f;
        }

        void TickPowerCooldowns(Faction f, float dt)
        {
            if (f.powerCd.Count == 0) return;
            foreach (var k in f.powerCd.Keys.ToList())
                if (f.powerCd[k] > 0) f.powerCd[k] = Mathf.Max(0f, f.powerCd[k] - dt);
        }

        public JObject PowerDef(string pid) => pid != null && powers.TryGetValue(pid, out var p) ? p : null;

        public string PowerName(string pid) => GameData.S(PowerDef(pid), "name", pid);

        public int PowerCost(string pid) => GameData.I(PowerDef(pid), "cost", 0);

        /// <summary>The full recharge time right now (leader, Capacitors, relays and Uplinks; at least 40% of the data value).</summary>
        public float PowerCooldown(string pid)
        {
            var p = PowerDef(pid);
            if (p == null) return 0f;
            var f = Player;
            float m = LeaderF("power_cooldown", 1f);
            if (f.research.Contains("capacitors")) m *= 0.75f;
            int owned = relays.Count(r => r.owner == "player");
            m *= Mathf.Max(0.4f, 1f - owned * GameData.F(GameData.Rts["relays"], "power_cut", 0.08f) - f.powerCut);
            return GameData.F(p, "cooldown", 60) * m;
        }

        /// <summary>Seconds until this power is ready again (0 = ready).</summary>
        public float PowerCdLeft(string pid) => Player.powerCd.TryGetValue(pid, out var t) ? t : 0f;

        /// <summary>Why the player can't use this power now, or null.</summary>
        public string CanPower(string pid)
        {
            if (PowerDef(pid) == null) return "Unknown power";
            float cd = PowerCdLeft(pid);
            if (cd > 0) return $"Recharging ({(int)cd}s)";
            if (Player.res < PowerCost(pid)) return "Not enough Supplies";
            return null;
        }

        /// <summary>A short line for tooltips (sim.py / scenes/rts.py power_desc).</summary>
        public string PowerDesc(string pid)
        {
            var p = PowerDef(pid);
            if (p == null) return "";
            string text;
            switch (pid)
            {
                case "orbital": text = $"Orbital laser: {GameData.F(p, "damage"):0} damage in a big circle after {GameData.F(p, "delay"):0.#}s."; break;
                case "cryo": text = $"Freezes aliens in the area ({(int)(GameData.F(p, "slow") * 100)}% slow for {GameData.F(p, "slow_time"):0.#}s)."; break;
                case "repair": text = $"Heals your units and buildings in the area by {(int)(GameData.F(p, "heal") * 100)}%."; break;
                default: text = "Leader power. " + GameData.S(p, "desc", ""); break;
            }
            return text + $"  Cooldown {(int)PowerCooldown(pid)}s.";
        }

        /// <summary>Uses a player power at a spot on the map. Returns an error message or null.</summary>
        public string CastPower(string pid, Vector2 at)
        {
            string problem = CanPower(pid);
            if (problem != null) return problem;
            var p = PowerDef(pid);
            var P = Player;
            Pay("player", PowerCost(pid));
            P.powerCd[pid] = PowerCooldown(pid);
            float dm = LeaderF("power_damage", 1f);
            float radius = GameData.F(p, "radius", 0f);
            fx?.Power("player", pid, at, radius);
            switch (pid)
            {
                case "orbital":
                    strikes.Add(new Strike
                    {
                        t = GameData.F(p, "delay", 1.6f), pos = at, r = radius, dmg = GameData.F(p, "damage") * dm,
                        faction = "player", cls = "bld", air = true, kind = "orbital", burn = LeaderB("orbital_burn"),
                    });
                    break;
                case "cryo":
                {
                    float t = GameData.F(p, "slow_time", 6) * LeaderF("cryo_time", 1f);
                    foreach (var e in EnemiesNear("player", at.x, at.y, radius))
                    {
                        Hit(e, GameData.F(p, "damage") * dm, "bld", null, "player");
                        e.ApplySlow(GameData.F(p, "slow", 0.6f), t);
                    }
                    break;
                }
                case "repair":
                {
                    float heal = LeaderF("repair_heal", GameData.F(p, "heal", 0.5f));
                    foreach (var e in All().ToList())
                        if (e.alive && e.faction == "player" && D2(at.x, at.y, e.x, e.y) <= (radius + e.radius) * (radius + e.radius))
                            e.hp = Mathf.Min(e.maxHp, e.hp + e.maxHp * heal);
                    break;
                }
                case "mech":
                    SpawnUnit("mech", "player", at.x, at.y);
                    break;
                case "turret_drop":
                {
                    string kind = GameData.S(p, "tower", "pulse");
                    int n = GameData.I(p, "count", 2);
                    for (int i = 0; i < n; i++)
                    {
                        float tx = at.x + (i - (n - 1) / 2f) * 50f, ty = at.y;
                        var t = new Tower(kind, "player", tx, ty, null, -1);
                        t.tier = Mathf.Min(GameData.I(p, "tier", 2), t.MaxTier - 1);
                        t.ApplyTier();
                        t.hp = t.maxHp;
                        t.buildLeft = 0;
                        t.lifetime = GameData.F(p, "duration", 60);
                        towers.Add(t);
                        fx?.Spawned(t);
                    }
                    break;
                }
                case "carpet":
                {
                    var home = bases.FirstOrDefault(b => b.Alive && b.faction == "player");
                    var from = home != null ? new Vector2(home.x, home.y) : at - new Vector2(200, 0);
                    var dir = at - from;
                    dir = dir.sqrMagnitude > 0.01f ? dir.normalized : Vector2.right;
                    int n = GameData.I(p, "count", 8);
                    float length = GameData.F(p, "length", 420);
                    for (int i = 0; i < n; i++)
                    {
                        float k = -length / 2f + length * i / Mathf.Max(1, n - 1);
                        strikes.Add(new Strike
                        {
                            t = 0.6f + i * 0.12f, pos = at + dir * k, r = radius, dmg = GameData.F(p, "damage") * dm,
                            faction = "player", cls = "bld", air = false, kind = "shell",
                        });
                    }
                    break;
                }
                case "firestorm":
                {
                    float life = GameData.F(p, "duration", 8);
                    zones.Add(new Zone { pos = at, r = radius, dps = GameData.F(p, "damage") * dm, t = life, faction = "player", kind = "fire" });
                    fx?.Zone(at, radius, life, "fire");
                    break;
                }
                case "deep_freeze":
                {
                    float stun = GameData.F(p, "stun", 4);
                    foreach (var e in EnemiesNear("player", at.x, at.y, radius))
                    {
                        e.stunT = Mathf.Max(e.stunT, stun * LeaderF("cryo_time", 1f));
                        e.ApplySlow(0.5f, stun + 3f);
                    }
                    break;
                }
                case "bulwark":
                {
                    float frac = GameData.F(p, "shield", 0.6f);
                    foreach (var e in All())
                        if (e.alive && !e.IsUnit && e.faction == "player")
                        {
                            e.shield = Mathf.Max(e.shield, e.maxHp * frac);
                            fx?.Ring(e.Pos, e.radius + 8, "energy");
                        }
                    break;
                }
                case "drone_swarm":
                {
                    var home = bases.FirstOrDefault(b => b.Alive && b.faction == "player");
                    var from = home != null ? new Vector2(home.x, home.y) : at;
                    var drones = new List<Unit>();
                    for (int i = 0; i < GameData.I(p, "count", 6); i++)
                        drones.Add(SpawnDrone(from + new Vector2(Random.Range(-40f, 40f), Random.Range(-40f, 40f)), GameData.F(p, "duration", 30)));
                    OrderMove(drones, at, true);
                    break;
                }
                case "supply_drop":
                {
                    int amount = GameData.I(p, "amount", 450);
                    P.res += amount;
                    fx?.Float(at, $"+{amount} Supplies", "supplies");
                    break;
                }
                case "sniper_net":
                {
                    scans.Add(new ScanArea { pos = at, r = 420, t = GameData.F(p, "reveal", 15) });
                    UpdateDetection();
                    UpdateFog();          // sim.py: the new scan counts at once
                    var foes = EnemiesNear("player", at.x, at.y, 650).Where(e => e.IsUnit && Seen(e, "player"))
                        .OrderByDescending(e => e.hp).Take(GameData.I(p, "targets", 6)).ToList();
                    foreach (var e in foes)
                    {
                        fx?.Power("player", "sniper_hit", e.Pos, e.radius);
                        Hit(e, GameData.F(p, "damage", 220) * dm, "hero", null, "player", true);
                    }
                    break;
                }
            }
            return null;
        }

        /// <summary>A Kestrel attack drone (sim.py SWARM_DRONE): a light flyer that leaves after its time.</summary>
        Unit SpawnDrone(Vector2 at, float life)
        {
            var u = SpawnUnit("swarm_drone", "player", at.x, at.y);
            u.name = "Attack Drone";
            u.cls = "air";
            u.flying = true;
            u.squad = 1;
            u.memberHp = u.maxHp = u.hp = DroneHp;
            u.armor = 0;
            u.speed = DroneSpeed;
            u.range = DroneRange;
            u.minRange = 0;
            u.damage = DroneDamage;
            u.rate = DroneRate;
            u.splash = 0;
            u.hitsAir = true;
            u.hitsGround = true;
            u.pop = 0;
            u.sight = DroneSight;
            u.radius = 16;
            u.lifetime = life;
            u.ability = null;
            u.bonus = null;
            u.x = at.x;
            u.y = at.y;
            return u;
        }

        // ------------------------------------------------------------------ alien powers
        public float AlienPowerCdLeft(string pid) => Alien.powerCd.TryGetValue(pid, out var t) ? t : 0f;

        /// <summary>The Swarm casts one of its powers (no cost, cooldown only). False if it is not ready.</summary>
        public bool AlienPower(string pid, Vector2 at)
        {
            var p = AlienPowers?[pid] as JObject;
            var f = Alien;
            if (p == null || AlienPowerCdLeft(pid) > 0) return false;
            Base hive = null;
            if (pid == "swarm_call")
            {
                hive = bases.Where(b => b.Alive && b.faction == "alien").OrderBy(b => D2(b.x, b.y, at.x, at.y)).FirstOrDefault();
                if (hive == null) return false;
            }
            f.powerCd[pid] = GameData.F(p, "cooldown", 100);
            float radius = GameData.F(p, "radius", 0f);
            fx?.Power("alien", pid, at, radius);
            switch (pid)
            {
                case "spore_storm":
                {
                    float life = GameData.F(p, "duration", 6);
                    zones.Add(new Zone { pos = at, r = radius, dps = GameData.F(p, "damage"), t = life, faction = "alien", kind = "spore" });
                    fx?.Zone(at, radius, life, "spore");
                    break;
                }
                case "acid_rain":
                    for (int i = 0; i < GameData.I(p, "count", 5); i++)
                        strikes.Add(new Strike
                        {
                            t = 0.8f + i * 0.25f,
                            pos = at + new Vector2(Random.Range(-radius, radius), Random.Range(-radius, radius)) * 0.7f,
                            r = 60, dmg = GameData.F(p, "damage"), faction = "alien", cls = "bld", air = true, kind = "acid",
                        });
                    break;
                case "swarm_call":
                {
                    string kind = GameData.S(p, "unit", "skitters");
                    var spawned = new List<Unit>();
                    for (int i = 0; i < GameData.I(p, "count", 2); i++)
                        spawned.Add(SpawnUnit(kind, "alien", hive.x + Random.Range(-60f, 60f), hive.y + Random.Range(-60f, 60f)));
                    OrderMove(spawned, at, true);
                    break;
                }
            }
            // the player hears about it only where he has explored or is looking (sim.py)
            if (Explored("player", at.x, at.y) || InView(at)) Alert(at, "Alien power: " + GameData.S(p, "name", pid) + "!", 0f);
            return true;
        }
    }
}
