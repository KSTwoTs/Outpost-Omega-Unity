using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;
using Outpost.Content;

namespace Outpost.Sim
{
    /// <summary>
    /// The Command-mode simulation, ported from game/rts/sim.py. This file holds the lists of everything, setup,
    /// Tick, victory and the spatial queries; each rules area is a partial file in Sim/Systems (Construction,
    /// Production, Orders, Units, Combat, Towers, Abilities, Support, Detection, Economy, MapControl).
    /// Everything runs in game pixels and seconds; the view layer only reads it.
    /// </summary>
    public partial class Battle
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
        public string leader = "vanguard";      // rts.json "leaders" passive for the player (change it with SetLeader)
        public float alienArmyMult = 1f;        // difficulty (rts.json ai.difficulties.army_mult)
        public readonly Dictionary<string, Faction> factions = new Dictionary<string, Faction>();
        public IBattleFx fx;
        public float time;
        public string winner;
        public AlienAI ai;
        /// <summary>How this battle was made (BattleConfig.Create); null in rules tests that call new Battle(map) directly.</summary>
        public BattleConfig config;

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
            SetupMapControl();
            SetupFog();
            hw = Mathf.CeilToInt(grid.PxW / Cell) + 1;
            hh = Mathf.CeilToInt(grid.PxH / Cell) + 1;
            cells = new List<Entity>[hw * hh];
            for (int i = 0; i < cells.Length; i++) cells[i] = new List<Entity>();
            Player.loadout.AddRange(TowerBehaviors.DefaultLoadout());   // rts.py default when no loadout is given
            SetLeader(leader);
        }

        public static bool Hostile(string a, string b) => a != b;

        public IEnumerable<Entity> All()
        {
            foreach (var u in units) yield return u;
            foreach (var b in buildings) yield return b;
            foreach (var t in towers) yield return t;
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
            TickFog(dt);
            UpdateMapControl(dt);
            for (int i = 0; i < buildings.Count; i++) if (buildings[i].alive) UpdateBuilding(buildings[i], dt);
            for (int i = 0; i < towers.Count; i++) if (towers[i].alive) UpdateTower(towers[i], dt);
            for (int i = 0; i < units.Count; i++) if (units[i].alive) UpdateUnit(units[i], dt);
            UpdateProjectiles(dt);
            UpdateStrikes(dt);
            UpdateZones(dt);
            ai?.Tick(dt);
            units.RemoveAll(u => !u.alive || u.inBunker != null);     // garrisoned squads leave the field
            buildings.RemoveAll(b => !b.alive);
            towers.RemoveAll(t => !t.alive);
            CheckVictory();
        }

        void CheckVictory()
        {
            if (!bases.Any(b => b.Alive && b.faction == "player")) winner = "alien";
            else if (!bases.Any(b => b.Alive && b.faction == "alien")) winner = "player";
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
    }
}
