// Fog.cs · roadmap step M1.7 · vision.
// Fog of war per faction (game/rts/world.py update_fog / visible / explored, sim.py update_fog every 0.2 s), what a
// faction has seen of an entity (sim.py seen), and the positioned alert log (sim.py alert / alerts) the HUD shows and
// pings on the minimap.
using System.Collections.Generic;
using UnityEngine;
using Outpost.Content;

namespace Outpost.Sim
{
    /// <summary>
    /// One faction's fog of war on the map's tile grid (world.py fog): 0 unexplored, 1 explored, 2 visible.
    /// A tile is visible if the last update stamped it; explored tiles stay explored for the rest of the match.
    /// </summary>
    public class FogGrid
    {
        public readonly int W, H;
        public readonly float T;
        readonly byte[] explored;
        readonly int[] seen;          // number of the last update that stamped the tile
        int gen = 1;
        /// <summary>Debug / Sandbox: everything counts as visible (Python reveal).</summary>
        public bool revealAll;
        /// <summary>Goes up after every update, so views know when to redraw.</summary>
        public int version;

        static readonly Dictionary<int, int[]> spans = new Dictionary<int, int[]>();

        public FogGrid(int w, int h, float t)
        {
            W = w;
            H = h;
            T = t;
            explored = new byte[w * h];
            seen = new int[w * h];
        }

        /// <summary>0 unexplored, 1 explored, 2 visible.</summary>
        public byte State(int i) => revealAll || seen[i] == gen ? (byte)2 : explored[i];

        public byte StateAt(int tx, int ty) => tx < 0 || ty < 0 || tx >= W || ty >= H ? (byte)0 : State(ty * W + tx);

        public bool Visible(float x, float y) => StateAt(Mathf.FloorToInt(x / T), Mathf.FloorToInt(y / T)) == 2;

        public bool Explored(float x, float y) => StateAt(Mathf.FloorToInt(x / T), Mathf.FloorToInt(y / T)) >= 1;

        /// <summary>Share of the map explored, 0..1.</summary>
        public float ExploredShare()
        {
            int n = 0;
            for (int i = 0; i < explored.Length; i++) if (explored[i] != 0) n++;
            return n / (float)explored.Length;
        }

        /// <summary>Starts an update: everything visible drops back to explored.</summary>
        public void Begin() => gen++;

        public void End() => version++;

        /// <summary>Marks the tiles within sight game pixels of (x, y) visible and explored. Same circle as Python:
        /// radius r = int(sight / T) tiles (at least 1), every tile with dx*dx + dy*dy &lt;= r*r + r.</summary>
        public void Stamp(float x, float y, float sight)
        {
            int cx = Mathf.FloorToInt(x / T), cy = Mathf.FloorToInt(y / T), r = Mathf.Max(1, (int)(sight / T));
            var half = Span(r);
            for (int dy = -r; dy <= r; dy++)
            {
                int ty = cy + dy;
                if (ty < 0 || ty >= H) continue;
                int hw = half[dy + r], x0 = Mathf.Max(0, cx - hw), x1 = Mathf.Min(W - 1, cx + hw), row = ty * W;
                for (int tx = x0; tx <= x1; tx++)
                {
                    seen[row + tx] = gen;
                    explored[row + tx] = 1;
                }
            }
        }

        /// <summary>Half-width of each row of the circle of radius r (index dy + r).</summary>
        static int[] Span(int r)
        {
            if (spans.TryGetValue(r, out var s)) return s;
            s = new int[2 * r + 1];
            for (int dy = -r; dy <= r; dy++)
            {
                int lim = r * r + r - dy * dy, hw = (int)Mathf.Sqrt(lim);
                while ((hw + 1) * (hw + 1) <= lim) hw++;
                while (hw > 0 && hw * hw > lim) hw--;
                s[dy + r] = hw;
            }
            spans[r] = s;
            return s;
        }

        /// <summary>Lifts the fog for good (Python reveal_all).</summary>
        public void RevealAll()
        {
            revealAll = true;
            for (int i = 0; i < explored.Length; i++) explored[i] = 1;
            version++;
        }
    }

    /// <summary>One entry of the alert log: when, where (game pixels) and what. faction picks the colour: alien = bad news.</summary>
    public class AlertNote
    {
        public float t;
        public Vector2 pos;
        public string text, faction;
    }

    public partial class Battle
    {
        /// <summary>Fog of war of each side. Python keeps only the player's; the alien grid is kept too (unused by
        /// the AI, which, like the Python AI, knows where everything is).</summary>
        public readonly Dictionary<string, FogGrid> fog = new Dictionary<string, FogGrid>();
        /// <summary>The player's camera view in game pixels, set by the game each frame (null headless).
        /// Units hit outside it raise Our forces are under attack (sim.py view).</summary>
        public Rect? view;
        /// <summary>Positioned alerts, oldest first (sim.py alerts). The minimap pings the last few.</summary>
        public readonly List<AlertNote> alerts = new List<AlertNote>();
        float lastAlert = -99f, fogT;
        /// <summary>Fog cost: number of updates, total and last time in milliseconds.</summary>
        public int fogUpdates;
        public double fogMsTotal, fogMsLast;
        readonly System.Diagnostics.Stopwatch fogWatch = new System.Diagnostics.Stopwatch();

        void SetupFog()
        {
            foreach (var n in new[] { "player", "alien" }) fog[n] = new FogGrid(grid.W, grid.H, grid.T);
        }

        public FogGrid FogOf(string faction) => fog.TryGetValue(faction, out var g) ? g : null;

        /// <summary>Lifts the fog for every side (Sandbox, debugging; Python reveal).</summary>
        public void RevealMap()
        {
            foreach (var g in fog.Values) g.RevealAll();
        }

        void TickFog(float dt)
        {
            fogT -= dt;
            if (fogT > 0) return;
            fogT = 0.2f;
            UpdateFog();
        }

        /// <summary>sim.py update_fog: every living unit, building and tower of a side sees its sight radius around it,
        /// plus the player's scans (Sensor Array, Sniper Net) and every relay the side owns (relays.sight).</summary>
        public void UpdateFog()
        {
            fogWatch.Reset();
            fogWatch.Start();
            float relaySight = GameData.F(GameData.Rts["relays"], "sight", 320);
            foreach (var kv in fog)
            {
                var g = kv.Value;
                string fac = kv.Key;
                if (g.revealAll) continue;
                g.Begin();
                for (int i = 0; i < units.Count; i++)
                {
                    var u = units[i];
                    if (u.alive && u.faction == fac) g.Stamp(u.x, u.y, u.sight);
                }
                for (int i = 0; i < buildings.Count; i++)
                {
                    var b = buildings[i];
                    if (b.alive && b.faction == fac) g.Stamp(b.x, b.y, b.sight);
                }
                for (int i = 0; i < towers.Count; i++)
                {
                    var t = towers[i];
                    if (t.alive && t.faction == fac) g.Stamp(t.x, t.y, t.sight);
                }
                if (fac == "player")
                    foreach (var s in scans) g.Stamp(s.pos.x, s.pos.y, s.r);
                foreach (var r in relays)
                    if (r.owner == fac) g.Stamp(r.x, r.y, relaySight);
                g.End();
            }
            fogWatch.Stop();
            fogMsLast = fogWatch.Elapsed.TotalMilliseconds;
            fogMsTotal += fogMsLast;
            fogUpdates++;
        }

        public bool Visible(string faction, float x, float y)
        {
            var g = FogOf(faction);
            return g == null || g.Visible(x, y);
        }

        public bool Explored(string faction, float x, float y)
        {
            var g = FogOf(faction);
            return g == null || g.Explored(x, y);
        }

        /// <summary>sim.py seen: can the faction see this entity right now? Its own things always; buildings and towers
        /// once their spot has been explored (they stay on the map as scouted); units only inside vision and not
        /// hidden by stealth or burrowing.</summary>
        public bool Seen(Entity e, string faction = "player")
        {
            if (e.faction == faction) return true;
            var g = FogOf(faction);
            if (g == null || g.revealAll) return true;
            if (!e.IsUnit) return g.Explored(e.x, e.y);
            if (HiddenFrom(e, faction)) return false;
            return g.Visible(e.x, e.y);
        }

        public bool InView(Vector2 p) => view.HasValue && view.Value.Contains(p);

        // ------------------------------------------------------------------ alerts
        /// <summary>sim.py alert: logs and shows an alert unless another one came less than gap seconds ago.</summary>
        public void Alert(Vector2 at, string text, float gap = 10f, string faction = "alien")
        {
            if (time - lastAlert < gap) return;
            lastAlert = time;
            Note(at, text, faction);
        }

        /// <summary>Logs and shows an alert with no throttle (sim.py alerts.append).</summary>
        public void Note(Vector2 at, string text, string faction = "player")
        {
            alerts.Add(new AlertNote { t = time, pos = at, text = text, faction = faction });
            if (alerts.Count > 100) alerts.RemoveAt(0);
            fx?.Message(faction, text);
        }
    }
}
