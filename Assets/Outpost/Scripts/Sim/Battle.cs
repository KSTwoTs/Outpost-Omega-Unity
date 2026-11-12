using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;
using Outpost.Content;

namespace Outpost.Sim
{
    /// <summary>What the battle tells the view layer about (spawns, deaths, shots).</summary>
    public interface IBattleFx
    {
        void Spawned(Entity e);
        void Died(Entity e);
        void Shot(Entity src, Vector2 from, Vector2 to, string faction, int count);
        void Impact(Vector2 at, string faction, float size, bool air);
        void ProjectileSpawned(Projectile p);
        void ProjectileEnded(Projectile p);
        void Lightning(List<Vector2> points, bool air);
        void Beam(Vector2 from, Vector2 to);
        void Message(string faction, string text);
        /// <summary>A quick ring on the ground. kind: blast / energy / stim / goo / burrow / upgrade.</summary>
        void Ring(Vector2 at, float radius, string kind);
        /// <summary>A patch that stays for a while. kind: fire / acid.</summary>
        void Zone(Vector2 at, float radius, float life, string kind);
        void Trail(Vector2 from, Vector2 to, string faction);
    }

    /// <summary>
    /// The Command-mode simulation, ported from game/rts/sim.py: bases, buildings and their upgrades,
    /// production, towers, unit orders, targeting, counters / armor / bonus damage, squads, projectiles,
    /// unit abilities and buffs, hero respawn, economy, research, healer and shield auras, stealth and
    /// detection.
    /// Everything runs in game pixels and seconds; the view layer only reads it.
    /// </summary>
    public class Battle
    {
        public readonly MapData map;
        public readonly NavGrid grid;
        public readonly List<Unit> units = new List<Unit>();
        public readonly List<Building> buildings = new List<Building>();
        public readonly List<Tower> towers = new List<Tower>();
        public readonly List<Base> bases = new List<Base>();
        public readonly List<Projectile> projectiles = new List<Projectile>();
        public readonly List<Strike> strikes = new List<Strike>();
        public readonly List<Zone> zones = new List<Zone>();
        public readonly List<ScanArea> scans = new List<ScanArea>();
        /// <summary>Cloaked unit ids each faction can currently see.</summary>
        public readonly Dictionary<string, HashSet<int>> detected = new Dictionary<string, HashSet<int>>
        {
            { "player", new HashSet<int>() }, { "alien", new HashSet<int>() },
        };
        public string leader = "vanguard";      // rts.json "leaders" passive for the player
        public float alienArmyMult = 1f;        // difficulty (rts.json ai.difficulties.army_mult)
        public readonly Dictionary<string, Faction> factions = new Dictionary<string, Faction>();
        public IBattleFx fx;
        public float time;
        public string winner;
        public AlienAI ai;

        public Faction Player => factions["player"];
        public Faction Alien => factions["alien"];

        // spatial hash
        const float Cell = 128f;
        readonly int hw, hh;
        readonly List<Entity>[] cells;
        readonly List<Entity> query = new List<Entity>();
        float recountT, supportT, detectT;

        public Battle(MapData map)
        {
            this.map = map;
            grid = new NavGrid(map);
            var st = GameData.Rts["start"];
            foreach (var n in new[] { "player", "alien" })
                factions[n] = new Faction { name = n, res = GameData.F(st, n == "player" ? "supplies" : "biomass", 500) };
            hw = Mathf.CeilToInt(grid.PxW / Cell) + 1;
            hh = Mathf.CeilToInt(grid.PxH / Cell) + 1;
            cells = new List<Entity>[hw * hh];
            for (int i = 0; i < cells.Length; i++) cells[i] = new List<Entity>();
        }

        public static bool Hostile(string a, string b) => a != b;

        public IEnumerable<Entity> All()
        {
            foreach (var u in units) yield return u;
            foreach (var b in buildings) yield return b;
            foreach (var t in towers) yield return t;
        }

        // ------------------------------------------------------------------ setup / construction
        public Base FoundBase(MapData.SiteData site, string faction, bool instant)
        {
            var b = new Base
            {
                faction = faction, site = site,
                slots = new Building[site.slots.Length], sockets = new Tower[site.sockets.Length],
            };
            var hq = new Building(faction == "player" ? "hq" : "hive", faction, site.x, site.y, b, -1);
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
            var t = new Tower(kind, b.faction, p[0], p[1], b, socket);
            t.angle = Mathf.Atan2(p[1] - b.y, p[0] - b.x);
            if (factions[b.faction].research.Contains("turret_plating"))
            {
                t.maxHp *= 1.4f;
                t.hp = t.maxHp * 0.1f;
            }
            if (instant) Finish(t);
            b.sockets[socket] = t;
            towers.Add(t);
            grid.BlockCircle(t.x, t.y, 14, 1);
            fx?.Spawned(t);
            return t;
        }

        public int BuildCost(string kind) => GameData.I(GameData.Building(kind), "cost", 100);

        public int TowerCost(string kind) =>
            kind == "spine" ? GameData.I(GameData.Rts["alien_tower"], "cost", 150)
                : Mathf.RoundToInt(GameData.F(GameData.Tower(kind), "cost", 100) * GameData.F(GameData.Rts["towers"], "cost_mult", 2f));

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
            if (!Pay(b.faction, BuildCost(kind))) return "Not enough " + factions[b.faction].ResName;
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
            if (!Pay(b.faction, TowerCost(kind))) return "Not enough " + factions[b.faction].ResName;
            PlaceTower(b, s, kind, false);
            return null;
        }

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
                        float ratio = t.Ratio;
                        t.maxHp *= 1.4f;
                        t.hp = t.maxHp * ratio;
                    }
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

        // ------------------------------------------------------------------ stealth and detection
        public static bool Stealthed(Entity e) => e is Unit u && (u.stealth || u.Has("camo"));

        /// <summary>True when the faction can't see or target e: burrowed, or cloaked and not detected.</summary>
        public bool HiddenFrom(Entity e, string faction)
        {
            if (!(e is Unit u) || u.faction == faction) return false;
            if (u.Has("burrowed")) return true;
            return Stealthed(u) && !(detected.TryGetValue(faction, out var seen) && seen.Contains(u.id));
        }

        /// <summary>Everything of a faction that reveals cloaked enemies, as (x, y, radius).</summary>
        List<Vector3> Detectors(string faction)
        {
            var cfg = GameData.Rts["detection"];
            float near = GameData.F(cfg, "near", 80);
            var res = new List<Vector3>();
            foreach (var u in units)
            {
                if (!u.alive || u.faction != faction) continue;
                float r = u.detect;
                if (u.kind == "commander") r = Mathf.Max(r, GameData.F(cfg, "commander", 200));
                else if (u.kind == "queen") r = Mathf.Max(r, GameData.F(cfg, "queen", 240));
                res.Add(new Vector3(u.x, u.y, Mathf.Max(r, near)));
            }
            foreach (var b in buildings)
            {
                if (!b.alive || b.faction != faction || !b.Done) continue;
                float r = GameData.F(b.data, "detect", b.kind == "hive" ? GameData.F(cfg, "hive", 200) : 0);
                if (r > 0) res.Add(new Vector3(b.x, b.y, r));
            }
            foreach (var t in towers)
                if (t.alive && t.faction == faction && t.Done && t.kind == "spine")
                    res.Add(new Vector3(t.x, t.y, GameData.F(cfg, "spine", 180)));
            if (faction == "player")
                foreach (var s in scans) res.Add(new Vector3(s.pos.x, s.pos.y, s.r));
            return res;
        }

        void UpdateDetection()
        {
            foreach (var kv in detected)
            {
                kv.Value.Clear();
                List<Vector3> dets = null;
                foreach (var u in units)
                {
                    if (!u.alive || u.faction == kv.Key || !Stealthed(u)) continue;
                    if (dets == null) dets = Detectors(kv.Key);
                    foreach (var d in dets)
                        if (D2(u.x, u.y, d.x, d.y) <= d.z * d.z)
                        {
                            kv.Value.Add(u.id);
                            break;
                        }
                }
            }
        }

        /// <summary>Sensor Array: reveals cloaked units around a spot anywhere on the map for a while.</summary>
        public string Scan(Building b, Vector2 at)
        {
            var sc = b.data?["scan"];
            if (sc == null || !b.Done) return "Can't scan";
            if (b.scanCd > 0) return $"Recharging ({(int)b.scanCd} s)";
            b.scanCd = GameData.F(sc, "cooldown", 45);
            float r = GameData.F(sc, "radius", 260);
            scans.Add(new ScanArea { pos = at, r = r, t = GameData.F(sc, "time", 12) });
            fx?.Zone(at, r, GameData.F(sc, "time", 12), "scan");
            UpdateDetection();
            return null;
        }

        // ------------------------------------------------------------------ support
        /// <summary>Healers (Medics, Menders, Sparrow) and Shield Drones; ticks every 0.5 s per unit.</summary>
        void UnitAuras(Unit u)
        {
            if (u.heal != null)
            {
                float radius = GameData.F(u.heal, "radius", 130);
                float amt = GameData.F(u.heal, "rate", 9) * 0.5f * (1 + 0.1f * u.rank);
                var classes = GameData.List(u.heal, "classes");
                foreach (var e in Near(u.x, u.y, radius))
                {
                    if (e == u || !e.alive || e.faction != u.faction || e.hp >= e.maxHp || !e.Done) continue;
                    if (System.Array.IndexOf(classes, e.cls) < 0) continue;
                    if (D2(u.x, u.y, e.x, e.y) > (radius + e.radius) * (radius + e.radius)) continue;
                    e.hp = Mathf.Min(e.maxHp, e.hp + amt * (e.cls == "bld" ? 3 : 1));
                    u.fireT = 0.3f;
                }
            }
            if (u.shieldAura != null)
            {
                float radius = GameData.F(u.shieldAura, "radius", 130), cap = GameData.F(u.shieldAura, "cap", 70);
                float add = GameData.F(u.shieldAura, "rate", 12) * 0.5f;
                foreach (var e in Near(u.x, u.y, radius))
                    if (e != u && e.alive && e is Unit && e.faction == u.faction && e.shield < cap &&
                        D2(u.x, u.y, e.x, e.y) <= (radius + e.radius) * (radius + e.radius))
                        e.shield = Mathf.Min(cap, e.shield + add);
            }
        }

        /// <summary>Shield Generators keep a shield on every building and tower around them; every 0.5 s.</summary>
        void UpdateSupport()
        {
            foreach (var g in buildings)
            {
                var sh = g.data?["shield"];
                if (sh == null || !g.alive || !g.Done) continue;
                float r = GameData.F(sh, "radius", 360), frac = GameData.F(sh, "frac", 0.3f), add = GameData.F(sh, "rate", 40) * 0.5f;
                foreach (var e in All())
                {
                    if (e.IsUnit || !e.alive || e.faction != g.faction || D2(e.x, e.y, g.x, g.y) > r * r) continue;
                    float cap = e.maxHp * frac;
                    if (e.shield < cap) e.shield = Mathf.Min(cap, e.shield + add);
                }
            }
        }

        public Unit SpawnUnit(string kind, string faction, float x, float y)
        {
            var mods = UnitMods(faction, kind);
            var u = new Unit(kind, faction, x, y, mods.hp);
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
            else
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

        // ------------------------------------------------------------------ abilities
        public bool AbilityReady(Unit u)
        {
            if (u.ability == null || u.abilityCd > 0) return false;
            string need = GameData.S(u.ability, "needs");
            return need == null || factions[u.faction].research.Contains(need);
        }

        /// <summary>Does this ability need a point on the ground (aimed), or does it just go off?</summary>
        public static bool AbilityAimed(Unit u) => u.ability != null && GameData.F(u.ability, "range") > 0;

        /// <summary>A unit's special ability. Aimed abilities take a point; others use the target or the unit.</summary>
        public bool UseAbility(Unit u, Vector2? at = null)
        {
            if (!u.alive || !AbilityReady(u)) return false;
            var ab = u.ability;
            string id = GameData.S(ab, "id", "");
            var p = at ?? (u.target != null && u.target.alive ? u.target.Pos : u.Pos);
            float rng = GameData.F(ab, "range");
            var toP = p - u.Pos;
            if (rng > 0 && toP.magnitude > rng) p = u.Pos + toP.normalized * rng;   // clamp to max range
            float dm = UnitMods(u.faction, u.kind).dmg;
            float dmg = GameData.F(ab, "damage"), radius = GameData.F(ab, "radius"), dur = GameData.F(ab, "duration");
            switch (id)
            {
                case "frag":
                case "canister":
                    Lob(u, p, dmg * dm, radius, 0.7f);
                    break;
                case "salvo":
                    for (int i = 0; i < 4; i++)
                        strikes.Add(new Strike { t = 0.25f + i * 0.15f, pos = p + Jitter(25), r = radius, dmg = dmg / 2 * dm,
                            faction = u.faction, cls = "air", from = u.Pos, air = true });
                    break;
                case "barrage":
                    for (int i = 0; i < 6; i++)
                        strikes.Add(new Strike { t = 0.4f + i * 0.2f, pos = p + Jitter(60), r = 55, dmg = dmg / 3 * dm,
                            faction = u.faction, cls = "air", from = u.Pos, air = false });
                    break;
                case "napalm":
                    zones.Add(new Zone { pos = p, r = radius, dps = dmg * dm, t = dur, faction = u.faction, kind = "fire" });
                    fx?.Zone(p, radius, dur, "fire");
                    fx?.Trail(u.Pos, p, u.faction);
                    break;
                case "stim":
                case "frenzy":
                case "lockdown":
                    u.buffs[id] = dur;
                    if (id == "lockdown")
                    {
                        u.path.Clear();
                        u.order = Order.Hold;
                    }
                    fx?.Ring(u.Pos, u.radius + 14, "stim");
                    break;
                case "ram":
                case "charge":
                    u.dash = new Dash { to = p, speed = u.speed * 3.2f, radius = radius, damage = dmg };
                    u.path.Clear();
                    break;
                case "jetpack":
                    u.dash = new Dash { to = p, speed = 520, radius = radius, damage = dmg, fly = true };
                    u.path.Clear();
                    fx?.Ring(u.Pos, 24, "blast");
                    break;
                case "stomp":
                    foreach (var e in EnemiesNear(u.faction, u.x, u.y, radius, false, true))
                    {
                        Hit(e, dmg * dm, u.cls, u, u.faction);
                        e.stunT = Mathf.Max(e.stunT, GameData.F(ab, "stun", 1f));
                    }
                    fx?.Ring(u.Pos, radius, "energy");
                    break;
                case "camo":
                    u.buffs["camo"] = dur;
                    fx?.Ring(u.Pos, 24, "goo");
                    break;
                case "stim_field":
                    foreach (var e in Near(u.x, u.y, radius).ToArray())
                        if (e is Unit o && o.alive && o.faction == u.faction && (o.cls == "inf" || o.cls == "hero") &&
                            D2(u.x, u.y, o.x, o.y) <= (radius + o.radius) * (radius + o.radius))
                            o.buffs["stim"] = dur;
                    fx?.Ring(u.Pos, radius, "stim");
                    break;
                case "burrow":
                    u.burrowTo = grid.NearestWalkablePx(grid.Clamp(p), 6);
                    u.buffs["burrowed"] = 0.8f + (p - u.Pos).magnitude / 380f;
                    u.path.Clear();
                    u.order = Order.None;
                    u.target = null;
                    fx?.Ring(u.Pos, 30, "burrow");
                    break;
                case "rally":
                    foreach (var e in Near(u.x, u.y, radius).ToArray())
                        if (e is Unit o && o.alive && o.faction == u.faction &&
                            D2(u.x, u.y, o.x, o.y) <= (radius + o.radius) * (radius + o.radius))
                            o.buffs["rally"] = dur;
                    fx?.Ring(u.Pos, radius, "energy");
                    break;
                case "brood":
                {
                    string spawn = GameData.S(ab, "spawn", "skitters");
                    for (int i = 0; i < (spawn == "skitters" ? 2 : 1); i++)
                    {
                        var j = Jitter(30);
                        var s = SpawnUnit(spawn, u.faction, u.x + j.x, u.y + j.y);
                        if (u.target != null && u.target.alive) OrderAttack(new[] { s }, u.target);
                    }
                    fx?.Ring(u.Pos, 34, "goo");
                    break;
                }
                default:
                    return false;
            }
            u.abilityCd = GameData.F(ab, "cooldown", 10);
            if (id != "camo") u.buffs.Remove("camo");
            return true;
        }

        static Vector2 Jitter(float r) => new Vector2(Random.Range(-r, r), Random.Range(-r, r));

        void Lob(Entity src, Vector2 to, float dmg, float radius, float flight)
        {
            var p = new Projectile
            {
                kind = "lob", pos = src.Pos, dest = to, dmg = dmg, splash = radius, cls = src.cls, faction = src.faction,
                src = src, hitsAir = false, lob = true, total = flight, bonus = (src as Unit)?.bonus,
            };
            fx?.ProjectileSpawned(p);
            projectiles.Add(p);
        }

        // ------------------------------------------------------------------ orders
        public static List<Vector2> Formation(int n, Vector2 c, float spacing)
        {
            int side = Mathf.Max(1, Mathf.CeilToInt(Mathf.Sqrt(n)));
            var r = new List<Vector2>();
            for (int i = 0; i < n; i++)
                r.Add(c + new Vector2((i % side - (side - 1) / 2f) * spacing, (i / side - (side - 1) / 2f) * spacing));
            return r;
        }

        public void OrderMove(IEnumerable<Unit> list, Vector2 to, bool attackMove)
        {
            var us = list.Where(u => u.alive).OrderBy(u => u.cls != "veh").ThenBy(u => u.cls != "inf").ThenBy(u => u.id).ToList();
            if (us.Count == 0) return;
            to = grid.Clamp(to);
            var spots = Formation(us.Count, to, 38);
            for (int i = 0; i < us.Count; i++)
            {
                var u = us[i];
                var s = spots[i];
                if (!u.flying) s = grid.NearestWalkablePx(s, 4);
                s = grid.Clamp(s);
                u.order = Order.Move;
                u.attackMove = attackMove;
                u.target = null;
                u.goal = s;
                u.guard = s;
                u.stuckT = 0;
                u.path = u.flying || grid.LineClear(u.Pos, s) ? new List<Vector2> { s } : grid.Path(u.Pos, s);
                if (u.path.Count == 0) u.path.Add(s);
            }
        }

        public void OrderAttack(IEnumerable<Unit> list, Entity target)
        {
            foreach (var u in list.ToList())
            {
                if (!u.alive) continue;
                if (u.CanHit(target))
                {
                    u.order = Order.Attack;
                    u.target = target;
                    u.path.Clear();
                    u.repath = 0;
                }
                else OrderMove(new[] { u }, target.Pos, true);
            }
        }

        public void OrderStop(IEnumerable<Unit> list, bool hold)
        {
            foreach (var u in list.ToList())
            {
                if (!u.alive) continue;
                u.order = hold ? Order.Hold : Order.None;
                u.path.Clear();
                u.target = null;
                u.attackMove = false;
                u.guard = u.Pos;
            }
        }

        // ------------------------------------------------------------------ tick
        public void Tick(float dt)
        {
            if (winner != null) return;
            time += dt;
            RebuildHash();
            Economy(dt);
            recountT -= dt;
            if (recountT <= 0) { recountT = 0.5f; Recount(); }
            supportT -= dt;
            if (supportT <= 0) { supportT = 0.5f; UpdateSupport(); }
            for (int i = scans.Count - 1; i >= 0; i--)
            {
                scans[i].t -= dt;
                if (scans[i].t <= 0) scans.RemoveAt(i);
            }
            detectT -= dt;
            if (detectT <= 0) { detectT = 0.25f; UpdateDetection(); }
            for (int i = 0; i < buildings.Count; i++) if (buildings[i].alive) UpdateBuilding(buildings[i], dt);
            for (int i = 0; i < towers.Count; i++) if (towers[i].alive) UpdateTower(towers[i], dt);
            for (int i = 0; i < units.Count; i++) if (units[i].alive) UpdateUnit(units[i], dt);
            UpdateProjectiles(dt);
            UpdateStrikes(dt);
            UpdateZones(dt);
            ai?.Tick(dt);
            units.RemoveAll(u => !u.alive);
            buildings.RemoveAll(b => !b.alive);
            towers.RemoveAll(t => !t.alive);
            CheckVictory();
        }

        void Economy(float dt)
        {
            foreach (var f in factions.Values)
            {
                float inc = 0;
                foreach (var b in buildings) if (b.alive && b.faction == f.name) inc += b.Income;
                f.income = inc * f.incomeMult;
                f.res += f.income * dt;
                // fallen heroes come back at the first base (the Queen only once the Swarm has Tech 3)
                if (f.hero == null && f.heroRespawn > 0)
                {
                    f.heroRespawn -= dt;
                    if (f.heroRespawn <= 0 && (f.name == "player" || f.tech >= 3))
                    {
                        f.heroRespawn = 0;
                        if (SpawnHero(f.name) != null && f.name == "player") fx?.Message("player", "Commander is back in the field");
                    }
                }
            }
        }

        void Recount()
        {
            var st = GameData.Rts["start"];
            foreach (var f in factions.Values)
            {
                int pop = 0;
                foreach (var u in units) if (u.alive && u.faction == f.name) pop += u.pop;
                foreach (var b in buildings) if (b.alive && b.faction == f.name) foreach (var q in b.queue) pop += q.pop;
                f.popUsed = pop;
                int nb = bases.Count(b => b.Alive && b.faction == f.name);
                bool forts = bases.Any(b => b.Alive && b.faction == f.name && b.hq.upgraded);
                f.popCap = Mathf.Min(GameData.I(st, "pop_max", 60),
                    GameData.I(st, "pop_base", 25) + GameData.I(st, "pop_per_base", 10) * Mathf.Max(0, nb - 1) + (forts ? 5 : 0));
                string core = f.name == "player" ? "reactor" : "node";
                f.tech = 1 + Mathf.Min(2, buildings.Count(b => b.alive && b.faction == f.name && b.kind == core && b.Done));
            }
        }

        void CheckVictory()
        {
            if (!bases.Any(b => b.Alive && b.faction == "player")) winner = "alien";
            else if (!bases.Any(b => b.Alive && b.faction == "alien")) winner = "player";
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

        // ------------------------------------------------------------------ spatial queries
        void RebuildHash()
        {
            foreach (var c in cells) c.Clear();
            foreach (var e in All())
                if (e.alive) cells[CellOf(e.x, e.y)].Add(e);
        }

        int CellOf(float x, float y)
        {
            int cx = Mathf.Clamp((int)(x / Cell), 0, hw - 1), cy = Mathf.Clamp((int)(y / Cell), 0, hh - 1);
            return cy * hw + cx;
        }

        /// <summary>Entities whose cell overlaps the circle (caller still checks distance). Reuses one list.</summary>
        public List<Entity> Near(float x, float y, float r)
        {
            query.Clear();
            int x0 = Mathf.Clamp((int)((x - r) / Cell), 0, hw - 1), x1 = Mathf.Clamp((int)((x + r) / Cell), 0, hw - 1);
            int y0 = Mathf.Clamp((int)((y - r) / Cell), 0, hh - 1), y1 = Mathf.Clamp((int)((y + r) / Cell), 0, hh - 1);
            for (int cy = y0; cy <= y1; cy++)
            for (int cx = x0; cx <= x1; cx++)
                query.AddRange(cells[cy * hw + cx]);
            return query;
        }

        static float D2(float ax, float ay, float bx, float by) => (ax - bx) * (ax - bx) + (ay - by) * (ay - by);

        Entity Acquire(string faction, float x, float y, float radius, System.Func<Entity, bool> canHit)
        {
            Entity best = null;
            float bd = float.MaxValue;
            foreach (var e in Near(x, y, radius + 60))
            {
                if (!e.alive || !Hostile(e.faction, faction) || !canHit(e) || HiddenFrom(e, faction)) continue;
                float d = D2(x, y, e.x, e.y);
                if (d > (radius + e.radius) * (radius + e.radius)) continue;
                float score = d + (e.IsUnit ? 0 : 160000);     // prefer units over buildings, then closest
                if (score < bd) { bd = score; best = e; }
            }
            return best;
        }

        // ------------------------------------------------------------------ damage
        /// <summary>Hostile entities touching the circle (a fresh list, safe to damage while looping).</summary>
        public List<Entity> EnemiesNear(string faction, float x, float y, float r, bool air = true, bool ground = true)
        {
            var res = new List<Entity>();
            foreach (var e in Near(x, y, r + 40))
            {
                if (!e.alive || !Hostile(e.faction, faction)) continue;
                if (e.flying ? !air : !ground) continue;
                if (D2(x, y, e.x, e.y) <= (r + e.radius) * (r + e.radius)) res.Add(e);
            }
            return res;
        }

        /// <summary>
        /// Applies damage with class counters, the attacker's bonus table and armor (armor never cuts
        /// more than 75%). Burrowed units can't be hurt.
        /// </summary>
        public float Hit(Entity t, float amount, string cls, Entity src, string faction, bool pierce = false, JObject bonus = null)
        {
            if (t == null || !t.alive || amount <= 0) return 0;
            if (t is Unit tu && tu.Has("burrowed")) return 0;
            float amt = amount * GameData.Counter(cls, t.cls) * (bonus != null ? GameData.F(bonus, t.cls, 1f) : 1f);
            if (!pierce) amt = Mathf.Max(amt * 0.25f, amt - t.armor);
            if (t.shield > 0)
            {
                float soak = Mathf.Min(t.shield, amt);
                t.shield -= soak;
                amt -= soak;
            }
            t.hp -= amt;
            t.lastHitT = 0;
            if (t.hp <= 0) Kill(t, src);
            return amt;
        }

        /// <summary>Area damage: full at the centre, falling to half at the edge.</summary>
        public void Splash(Vector2 at, float r, float amount, string cls, Entity src, string faction, bool hitsAir, JObject bonus = null)
        {
            foreach (var e in EnemiesNear(faction, at.x, at.y, r, hitsAir, true))
            {
                float d = Mathf.Sqrt(D2(at.x, at.y, e.x, e.y));
                float fall = 1f - 0.5f * Mathf.Min(1f, d / Mathf.Max(1f, r));
                Hit(e, amount * fall, cls, src, faction, false, bonus);
            }
        }

        public void Kill(Entity e, Entity src)
        {
            if (!e.alive) return;
            e.alive = false;
            e.hp = 0;
            if (src is Unit k && k.alive)
            {
                k.kills++;
                var vet = GameData.Rts["veterancy"];
                var thr = vet?["kills"];
                int rank = thr != null ? thr.Count(v => k.kills >= (int)v) : 0;
                if (rank > k.rank)
                {
                    // each rank also toughens the squad
                    float hpStep = GameData.F(vet, "hp", 0.1f), ratio = k.Ratio;
                    k.memberHp *= (1 + hpStep * rank) / (1 + hpStep * k.rank);
                    k.maxHp = k.memberHp * k.squad;
                    k.hp = k.maxHp * ratio;
                    k.rank = rank;
                }
            }
            if (src != null && factions.TryGetValue(src.faction, out var kf)) kf.kills++;
            var ef = factions[e.faction];
            ef.losses++;
            if (e == ef.hero)
            {
                ef.hero = null;
                ef.heroRespawn = GameData.F(e.data, "respawn", 30);
                if (e.faction == "player") fx?.Message("alien", $"Commander down! Respawning at HQ in {(int)ef.heroRespawn} s");
            }
            if (e is Building b)
            {
                grid.BlockCircle(b.x, b.y, b.IsHq ? 44 : 26, -1);
                if (b.IsHq)
                {
                    // the base falls with its core
                    foreach (var s in b.baseRef.slots) if (s != null && s.alive) Kill(s, src);
                    foreach (var s in b.baseRef.sockets) if (s != null && s.alive) Kill(s, src);
                }
                else if (b.baseRef.slots[b.slot] == b) b.baseRef.slots[b.slot] = null;
                foreach (var q in b.queue)
                {
                    factions[b.faction].popUsed -= q.pop;
                    if (q.type == "research") factions[b.faction].researching.Remove(q.id);
                }
                b.queue.Clear();
            }
            if (e is Tower t)
            {
                grid.BlockCircle(t.x, t.y, 14, -1);
                if (t.baseRef.sockets[t.socket] == t) t.baseRef.sockets[t.socket] = null;
            }
            fx?.Died(e);
        }

        // ------------------------------------------------------------------ towers
        void UpdateTower(Tower t, float dt)
        {
            t.TickStatus(dt);
            if (!t.Done)
            {
                t.buildLeft -= dt;
                t.hp = Mathf.Min(t.maxHp, t.hp + t.maxHp * 0.9f * dt / t.buildTotal);
                return;
            }
            if (t.damage <= 0 || t.stunT > 0) return;
            if (t.cooldown > 0) t.cooldown -= dt;
            var tg = t.target;
            if (tg == null || !tg.alive || HiddenFrom(tg, t.faction) || D2(t.x, t.y, tg.x, tg.y) > (t.range + tg.radius) * (t.range + tg.radius))
                tg = t.target = Acquire(t.faction, t.x, t.y, t.range, t.CanHit);
            if (tg == null) return;
            float want = Mathf.Atan2(tg.y - t.y, tg.x - t.x);
            t.angle = Mathf.LerpAngle(t.angle * Mathf.Rad2Deg, want * Mathf.Rad2Deg, 1 - Mathf.Exp(-10 * dt)) * Mathf.Deg2Rad;
            if (t.cooldown > 0) return;
            t.cooldown = 1f / Mathf.Max(0.05f, t.rate);
            switch (t.behavior)
            {
                case "chain":
                {
                    var pts = new List<Vector2> { t.Pos, tg.Pos };
                    var hitSet = new HashSet<Entity> { tg };
                    Hit(tg, t.damage, "bld", t, t.faction);
                    var cur = tg;
                    for (int i = 1; i < Mathf.Max(1, t.chain); i++)
                    {
                        Entity next = null;
                        float bd = t.chainRange * t.chainRange;
                        foreach (var e in Near(cur.x, cur.y, t.chainRange))
                        {
                            if (!e.alive || hitSet.Contains(e) || !Hostile(e.faction, t.faction) || !t.CanHit(e)) continue;
                            float d = D2(cur.x, cur.y, e.x, e.y);
                            if (d < bd) { bd = d; next = e; }
                        }
                        if (next == null) break;
                        hitSet.Add(next);
                        pts.Add(next.Pos);
                        Hit(next, t.damage * Mathf.Pow(0.85f, i), "bld", t, t.faction);
                        cur = next;
                    }
                    fx?.Lightning(pts, tg.flying);
                    break;
                }
                case "pierce":
                {
                    var dir = (tg.Pos - t.Pos).normalized;
                    var end = t.Pos + dir * t.range;
                    foreach (var e in Near((t.x + end.x) / 2, (t.y + end.y) / 2, t.range / 2 + 40).ToArray())
                    {
                        if (!e.alive || !Hostile(e.faction, t.faction) || e.flying) continue;
                        var rel = e.Pos - t.Pos;
                        float along = Vector2.Dot(rel, dir);
                        if (along < 0 || along > t.range) continue;
                        if ((rel - dir * along).magnitude <= e.radius + 8) Hit(e, t.damage, "bld", t, t.faction, true);
                    }
                    fx?.Beam(t.Pos, end);
                    break;
                }
                default:
                    projectiles.Add(NewProjectile(t.behavior == "missile" ? "rocket" : t.kind == "spine" ? "acid" : "bolt",
                        t, tg, t.damage, t.splash, "bld", t.hitsAir, t.projectileSpeed));
                    break;
            }
        }

        Projectile NewProjectile(string kind, Entity src, Entity tg, float dmg, float splash, string cls, bool air, float speed)
        {
            var p = new Projectile
            {
                kind = kind, pos = src.Pos, target = tg, dest = tg.Pos, speed = speed, dmg = dmg, splash = splash,
                cls = cls, faction = src.faction, src = src, hitsAir = air, bonus = (src as Unit)?.bonus,
            };
            fx?.ProjectileSpawned(p);
            return p;
        }

        void UpdateProjectiles(float dt)
        {
            for (int i = projectiles.Count - 1; i >= 0; i--)
            {
                var p = projectiles[i];
                if (p.target != null && p.target.alive && !p.lob) p.dest = p.target.Pos;
                bool arrived;
                if (p.lob)
                {
                    p.t += dt;
                    arrived = p.t >= p.total;
                }
                else
                {
                    var d = p.dest - p.pos;
                    float step = p.speed * dt;
                    arrived = d.magnitude <= step + 2;
                    p.pos = arrived ? p.dest : p.pos + d.normalized * step;
                }
                if (!arrived) continue;
                projectiles.RemoveAt(i);
                fx?.ProjectileEnded(p);
                if (p.splash > 0)
                {
                    Splash(p.dest, p.splash, p.dmg, p.cls, p.src, p.faction, p.hitsAir, p.bonus);
                    fx?.Impact(p.dest, p.faction, p.splash, false);
                }
                else
                {
                    if (p.target != null && p.target.alive) Hit(p.target, p.dmg, p.cls, p.src, p.faction, false, p.bonus);
                    fx?.Impact(p.dest, p.faction, 10, p.target != null && p.target.flying);
                }
            }
        }

        void UpdateStrikes(float dt)
        {
            for (int i = strikes.Count - 1; i >= 0; i--)
            {
                var s = strikes[i];
                s.t -= dt;
                if (s.t > 0) continue;
                strikes.RemoveAt(i);
                Splash(s.pos, s.r, s.dmg, s.cls, null, s.faction, s.air);
                fx?.Trail(s.from, s.pos, s.faction);
                fx?.Impact(s.pos, s.faction, s.r, false);
            }
        }

        static readonly JObject ZoneBonus = new JObject { ["inf"] = 1.3f };

        void UpdateZones(float dt)
        {
            for (int i = zones.Count - 1; i >= 0; i--)
            {
                var z = zones[i];
                z.t -= dt;
                if (z.t <= 0) { zones.RemoveAt(i); continue; }
                z.tick -= dt;
                if (z.tick > 0) continue;
                z.tick = 0.25f;
                foreach (var e in EnemiesNear(z.faction, z.pos.x, z.pos.y, z.r, false, true))
                    Hit(e, z.dps * 0.25f, "bld", null, z.faction, z.kind == "acid" || z.kind == "spore", ZoneBonus);
            }
        }

        // ------------------------------------------------------------------ units
        void UpdateUnit(Unit u, float dt)
        {
            u.TickStatus(dt);
            u.TickBuffs(dt);
            u.moving = false;
            if (u.lifetime >= 0)
            {
                u.lifetime -= dt;
                if (u.lifetime <= 0)
                {
                    // timed units (Mech Drop, drones) just leave
                    u.alive = false;
                    fx?.Ring(u.Pos, 30, "energy");
                    fx?.Died(u);
                    return;
                }
            }
            if (u.burrowTo.HasValue && !u.Has("burrowed"))
            {
                // tunnelling units erupt at the far end
                var bt = u.burrowTo.Value;
                u.burrowTo = null;
                u.x = bt.x;
                u.y = bt.y;
                float er = GameData.F(u.ability, "radius", 60);
                Splash(u.Pos, er, GameData.F(u.ability, "damage", 50), u.cls, u, u.faction, false);
                fx?.Ring(u.Pos, er, "burrow");
                u.guard = u.lastPos = u.Pos;
            }
            if (u.Has("burrowed") || u.stunT > 0) return;
            if (u.cooldown > 0) u.cooldown -= dt;
            if (u.fireT > 0) u.fireT -= dt;
            if (u.heal != null || u.shieldAura != null)
            {
                u.auraT -= dt;
                if (u.auraT <= 0)
                {
                    u.auraT = 0.5f;
                    UnitAuras(u);
                }
            }

            // reinforcement: squads near one of their own base cores refill
            var rf = GameData.Rts["reinforce"];
            if (u.hp < u.maxHp && u.lastHitT > GameData.F(rf, "delay", 4) && u.lifetime < 0)
                foreach (var b in bases)
                    if (b.faction == u.faction && b.Alive && b.hq.Done &&
                        D2(u.x, u.y, b.x, b.y) < Mathf.Pow(GameData.F(rf, "radius", 330), 2))
                    {
                        u.hp = Mathf.Min(u.maxHp, u.hp + u.maxHp * GameData.F(rf, "rate", 0.05f) * dt);
                        break;
                    }

            // dash abilities (Ram, Charge, Jetpack)
            if (u.dash != null)
            {
                var ds = u.dash;
                ds.t += dt;
                bool wasFlying = u.flying;
                if (ds.fly) u.flying = true;
                bool arrived = MoveToward(u, ds.to, dt, 10, ds.speed);
                u.flying = wasFlying;
                if (arrived && ds.fly && !grid.WalkablePx(u.x, u.y, true))
                {
                    var land = grid.NearestWalkablePx(u.Pos, 6);
                    u.x = land.x;
                    u.y = land.y;
                }
                foreach (var e in EnemiesNear(u.faction, u.x, u.y, u.radius + 6, false, true))
                    if (e is Unit && e.cls == "inf" && ds.hit.Add(e)) Hit(e, ds.damage, u.cls, u, u.faction);
                bool blocked = (u.Pos - u.lastPos).sqrMagnitude < 0.01f;
                if (arrived || blocked || ds.t > 2.5f)
                {
                    Splash(u.Pos, ds.radius, ds.damage, u.cls, u, u.faction, false);
                    fx?.Ring(u.Pos, ds.radius, "blast");
                    u.dash = null;
                    u.guard = u.Pos;
                }
                u.lastPos = u.Pos;
                return;
            }

            // targeting
            var t = u.target;
            if (t != null && (!t.alive || !u.CanHit(t) || HiddenFrom(t, u.faction)))
            {
                t = u.target = null;
                if (u.order == Order.Attack) { u.order = Order.None; u.guard = u.Pos; }
            }
            u.retarget -= dt;
            if (u.retarget <= 0)
            {
                u.retarget = 0.3f;
                if (u.order == Order.None || u.order == Order.Hold || (u.order == Order.Move && u.attackMove))
                {
                    float radius = u.order == Order.Hold ? u.AttackRange : Mathf.Max(u.sight, u.AttackRange);
                    var cand = u.damage > 0 ? Acquire(u.faction, u.x, u.y, radius, u.CanHit) : null;
                    if (cand != null && (t == null || D2(u.x, u.y, cand.x, cand.y) < D2(u.x, u.y, t.x, t.y) * 0.6f))
                        t = u.target = cand;
                }
                else if (u.order == Order.Move && t == null && u.damage > 0)
                {
                    var cand = Acquire(u.faction, u.x, u.y, u.AttackRange, u.CanHit);   // shoot while walking
                    if (cand != null) t = u.target = cand;
                }
            }

            bool inRange = false;
            if (t != null)
            {
                float d2 = D2(u.x, u.y, t.x, t.y), reach = u.AttackRange + t.radius;
                inRange = d2 <= reach * reach && d2 >= u.minRange * u.minRange;
                if (inRange)
                {
                    u.angle = Mathf.Atan2(t.y - u.y, t.x - u.x);
                    if (u.cooldown <= 0)
                    {
                        u.cooldown = 1f / Mathf.Max(0.05f, u.FireRate);
                        Fire(u, t);
                    }
                }
                else if (u.order == Order.Move && !u.attackMove && D2(u.x, u.y, t.x, t.y) > Mathf.Pow(u.AttackRange + t.radius + 60, 2))
                    u.target = t = null;   // out of reach while walking: forget it
            }

            // movement
            switch (u.order)
            {
                case Order.Move:
                    if (u.attackMove && t != null)
                    {
                        if (!inRange) Chase(u, t, dt);
                    }
                    else if (FollowPath(u, dt))
                    {
                        u.order = Order.None;
                        u.attackMove = false;
                        u.guard = u.Pos;
                    }
                    break;
                case Order.Attack:
                    if (t != null && !inRange) Chase(u, t, dt);
                    break;
                case Order.None:
                    if (t != null && !inRange)
                    {
                        if (D2(u.x, u.y, u.guard.x, u.guard.y) > Mathf.Pow(u.sight + 220, 2))
                        {
                            u.target = null;
                            OrderMove(new[] { u }, u.guard, false);
                        }
                        else Chase(u, t, dt);
                    }
                    else if (t == null && D2(u.x, u.y, u.guard.x, u.guard.y) > 60 * 60)
                        OrderMove(new[] { u }, u.guard, false);
                    break;
            }

            // stuck detection
            if (u.moving)
            {
                if ((u.Pos - u.lastPos).sqrMagnitude < Mathf.Pow(u.Speed * dt * 0.15f, 2))
                {
                    u.stuckT += dt;
                    if (u.stuckT > 1.5f)
                    {
                        u.stuckT = 0;
                        if (u.path.Count <= 1 && u.goal.HasValue && D2(u.x, u.y, u.goal.Value.x, u.goal.Value.y) < 90 * 90)
                            u.path.Clear();
                        else if (u.path.Count > 0)
                            u.path = grid.Path(u.Pos, u.path[u.path.Count - 1]);
                    }
                }
                else u.stuckT = 0;
            }
            u.lastPos = u.Pos;
            Separate(u, dt);
            // alien units use their abilities on their own
            if (u.faction != "player" && u.ability != null && t != null && t.alive && inRange && AbilityReady(u))
                UseAbility(u, t.Pos);
        }

        void Fire(Unit u, Entity t)
        {
            u.fireT = 0.16f;
            float dmg = u.damage * UnitMods(u.faction, u.kind).dmg * u.Members
                        * (1 + GameData.F(GameData.Rts["veterancy"], "damage", 0.15f) * u.rank);
            if (u.Has("rally")) dmg *= 1.3f;
            u.buffs.Remove("camo");
            if (u.cls == "inf" && t.cls == "inf" && factions[u.faction].research.Contains("hollow_point")) dmg *= 1.2f;
            if (!u.flying && grid.HighGround(u.x, u.y)) dmg *= 1.1f;
            if (u.range <= 50)
            {
                float dealt = Hit(t, dmg, u.cls, u, u.faction, false, u.bonus);
                if (u.lifesteal > 0 && dealt > 0) u.hp = Mathf.Min(u.maxHp, u.hp + dealt * u.lifesteal);
                if (u.splash > 0) Splash(t.Pos, u.splash, dmg * 0.5f, u.cls, u, u.faction, u.hitsAir, u.bonus);
                fx?.Impact(t.Pos, u.faction, 6, t.flying);
                return;
            }
            switch (u.projectile)
            {
                case "bullet":
                    fx?.Shot(u, u.Pos, t.Pos, u.faction, Mathf.Min(4, u.Members));
                    Hit(t, dmg, u.cls, u, u.faction, false, u.bonus);
                    if (u.splash > 0) Splash(t.Pos, u.splash, dmg * 0.4f, u.cls, u, u.faction, u.hitsAir, u.bonus);
                    if (u.burn && t.alive) t.ApplySlow(0.15f, 1f);
                    break;
                case "lob":
                {
                    var p = NewProjectile("lob", u, t, dmg, Mathf.Max(30, u.splash), u.cls, u.hitsAir, 0);
                    p.lob = true;
                    p.dest = t.Pos;
                    p.target = null;
                    p.total = u.range > 200 ? 0.9f : 0.5f;
                    projectiles.Add(p);
                    break;
                }
                default:
                {
                    float speed = u.projectile == "rocket" ? 420 : u.projectile == "shell" ? 520 : u.projectile == "flak" ? 600
                        : u.projectile == "acid" ? 340 : 480;
                    projectiles.Add(NewProjectile(u.projectile, u, t, dmg, u.splash, u.cls, u.hitsAir, speed));
                    break;
                }
            }
        }

        bool MoveToward(Unit u, Vector2 to, float dt, float stop, float speedOverride = -1f)
        {
            var d = to - u.Pos;
            float dist = d.magnitude;
            if (dist <= stop) return true;
            float speed = speedOverride > 0 ? speedOverride : u.Speed;
            if (speed <= 0) return false;
            float step = speed * dt * (u.flying ? 1f : grid.SpeedMult(u.x, u.y));
            step = Mathf.Min(step, dist);
            var n = u.Pos + d / dist * step;
            u.angle = Mathf.Atan2(d.y, d.x);
            Place(u, n);
            u.moving = true;
            return dist - step <= stop;
        }

        void Place(Unit u, Vector2 n)
        {
            if (u.flying)
            {
                n = grid.Clamp(n);
                u.x = n.x;
                u.y = n.y;
                return;
            }
            bool hereOk = grid.WalkablePx(u.x, u.y, true);
            if (grid.WalkablePx(n.x, n.y, true) || !hereOk) { u.x = n.x; u.y = n.y; }
            else if (grid.WalkablePx(n.x, u.y, true)) u.x = n.x;
            else if (grid.WalkablePx(u.x, n.y, true)) u.y = n.y;
            var c = grid.Clamp(u.Pos);
            u.x = c.x;
            u.y = c.y;
        }

        bool FollowPath(Unit u, float dt)
        {
            if (u.path.Count == 0) return true;
            bool last = u.path.Count == 1;
            if (MoveToward(u, u.path[0], dt, last ? 6 : 14)) u.path.RemoveAt(0);
            return u.path.Count == 0;
        }

        void Chase(Unit u, Entity t, float dt)
        {
            if (u.flying || grid.LineClear(u.Pos, t.Pos))
            {
                u.path.Clear();
                MoveToward(u, t.Pos, dt, 0);
                return;
            }
            u.repath -= dt;
            if (u.repath <= 0 || u.path.Count == 0)
            {
                u.path = grid.Path(u.Pos, t.Pos);
                u.repath = 1f + Random.value * 0.5f;
            }
            FollowPath(u, dt);
        }

        void Separate(Unit u, float dt)
        {
            float px = 0, py = 0;
            foreach (var o in Near(u.x, u.y, u.radius * 2 + 40))
            {
                if (o == u || !o.alive || o.flying != u.flying) continue;
                float dx = u.x - o.x, dy = u.y - o.y;
                float minD = (u.radius + o.radius) * (o.IsUnit ? 0.8f : 1f);
                float d2 = dx * dx + dy * dy;
                if (d2 >= minD * minD) continue;
                float d = Mathf.Sqrt(d2);
                if (d < 0.02f) { dx = Random.Range(-1f, 1f); dy = Random.Range(-1f, 1f); d = 1; }
                float strength = (minD - d) / minD, k = o.IsUnit ? 1f : 3f;
                px += dx / d * strength * k;
                py += dy / d * strength * k;
            }
            if (px != 0 || py != 0) Place(u, u.Pos + new Vector2(px, py) * 90 * dt);
        }
    }
}
