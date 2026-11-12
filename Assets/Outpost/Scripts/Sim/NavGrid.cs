using System.Collections.Generic;
using UnityEngine;
using Outpost.Content;

namespace Outpost.Sim
{
    /// <summary>
    /// Tile grid for ground pathing, same rules as the Python world: rock, hazard and crystal tiles block,
    /// buildings block a circle of tiles. 8-directional A* without corner cutting, then the path is
    /// shortened with line-of-sight checks. All positions are game pixels (x right, y down).
    /// </summary>
    public class NavGrid
    {
        public readonly int W, H;
        public readonly float T;
        readonly char[] tiles;
        readonly bool[] rock;
        readonly int[] blocked;

        // A* scratch
        readonly float[] g;
        readonly int[] came, stamp;
        readonly bool[] closed;
        int run;
        readonly MinHeap open = new MinHeap();

        static readonly int[] DX = { 1, -1, 0, 0, 1, 1, -1, -1 };
        static readonly int[] DY = { 0, 0, 1, -1, 1, -1, 1, -1 };
        static readonly float[] COST = { 1, 1, 1, 1, 1.41421f, 1.41421f, 1.41421f, 1.41421f };

        public NavGrid(MapData m)
        {
            W = m.w;
            H = m.h;
            T = m.tile;
            tiles = new char[W * H];
            rock = new bool[W * H];
            blocked = new int[W * H];
            for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                char c = x < m.tiles[y].Length ? m.tiles[y][x] : 'X';
                tiles[y * W + x] = c;
                rock[y * W + x] = c == 'X' || c == '~' || c == 'K';
            }
            g = new float[W * H];
            came = new int[W * H];
            stamp = new int[W * H];
            closed = new bool[W * H];
        }

        public float PxW => W * T;
        public float PxH => H * T;

        public bool InBounds(int x, int y) => x >= 0 && y >= 0 && x < W && y < H;

        public bool Walkable(int x, int y, bool ignoreBlocked = false) =>
            InBounds(x, y) && !rock[y * W + x] && (ignoreBlocked || blocked[y * W + x] == 0);

        public bool WalkablePx(float x, float y, bool ignoreBlocked = false) =>
            Walkable(Mathf.FloorToInt(x / T), Mathf.FloorToInt(y / T), ignoreBlocked);

        public char TileAt(float x, float y)
        {
            int tx = Mathf.FloorToInt(x / T), ty = Mathf.FloorToInt(y / T);
            return InBounds(tx, ty) ? tiles[ty * W + tx] : 'X';
        }

        public bool HighGround(float x, float y) => TileAt(x, y) == 'H';
        public float SpeedMult(float x, float y) => TileAt(x, y) == 'P' ? 1.1f : 1f;

        public Vector2 Center(int tx, int ty) => new Vector2((tx + 0.5f) * T, (ty + 0.5f) * T);

        public Vector2 Clamp(Vector2 p) => new Vector2(Mathf.Clamp(p.x, 4, PxW - 4), Mathf.Clamp(p.y, 4, PxH - 4));

        /// <summary>Adds (delta 1) or removes (delta -1) a building footprint.</summary>
        public void BlockCircle(float x, float y, float radius, int delta)
        {
            int r = (int)(radius / T) + 1, cx = (int)(x / T), cy = (int)(y / T);
            for (int ty = cy - r; ty <= cy + r; ty++)
            for (int tx = cx - r; tx <= cx + r; tx++)
            {
                if (!InBounds(tx, ty)) continue;
                var c = Center(tx, ty);
                if ((c - new Vector2(x, y)).magnitude <= radius)
                    blocked[ty * W + tx] = Mathf.Max(0, blocked[ty * W + tx] + delta);
            }
        }

        public Vector2Int NearestWalkable(int tx, int ty, int maxR = 12)
        {
            if (Walkable(tx, ty)) return new Vector2Int(tx, ty);
            for (int r = 1; r <= maxR; r++)
            {
                Vector2Int best = new Vector2Int(-1, -1);
                float bd = float.MaxValue;
                for (int y = ty - r; y <= ty + r; y++)
                for (int x = tx - r; x <= tx + r; x++)
                {
                    if (Mathf.Max(Mathf.Abs(x - tx), Mathf.Abs(y - ty)) != r || !Walkable(x, y)) continue;
                    float d = (x - tx) * (x - tx) + (y - ty) * (y - ty);
                    if (d < bd) { bd = d; best = new Vector2Int(x, y); }
                }
                if (best.x >= 0) return best;
            }
            return new Vector2Int(Mathf.Clamp(tx, 0, W - 1), Mathf.Clamp(ty, 0, H - 1));
        }

        public Vector2 NearestWalkablePx(Vector2 p, int maxR = 12)
        {
            if (WalkablePx(p.x, p.y)) return p;
            var t = NearestWalkable(Mathf.FloorToInt(p.x / T), Mathf.FloorToInt(p.y / T), maxR);
            return Center(t.x, t.y);
        }

        /// <summary>True when a ground unit can walk straight from a to b.</summary>
        public bool LineClear(Vector2 a, Vector2 b)
        {
            float d = (b - a).magnitude;
            int n = Mathf.Max(1, Mathf.CeilToInt(d / (T * 0.33f)));
            for (int i = 1; i <= n; i++)
            {
                var p = Vector2.Lerp(a, b, i / (float)n);
                if (!WalkablePx(p.x, p.y)) return false;
            }
            return true;
        }

        /// <summary>Waypoints (pixels) from a to b, not including a. Falls back to the closest reachable tile.</summary>
        public List<Vector2> Path(Vector2 a, Vector2 b, int maxNodes = 40000)
        {
            var result = new List<Vector2>();
            int sx = Mathf.Clamp(Mathf.FloorToInt(a.x / T), 0, W - 1), sy = Mathf.Clamp(Mathf.FloorToInt(a.y / T), 0, H - 1);
            if (!Walkable(sx, sy))
            {
                var s2 = NearestWalkable(sx, sy, 4);   // standing in a footprint: start from the nearest open tile
                sx = s2.x;
                sy = s2.y;
            }
            var goalT = NearestWalkable(Mathf.FloorToInt(b.x / T), Mathf.FloorToInt(b.y / T), 10);
            bool goalExact = WalkablePx(b.x, b.y);
            int start = sy * W + sx, goal = goalT.y * W + goalT.x;
            if (start == goal) { result.Add(goalExact ? b : Center(goalT.x, goalT.y)); return result; }

            run++;
            open.Clear();
            g[start] = 0;
            came[start] = -1;
            stamp[start] = run;
            closed[start] = false;
            open.Push(start, H0(sx, sy, goalT));
            int best = start;
            float bestH = H0(sx, sy, goalT);
            int expanded = 0;
            bool found = false;
            while (open.Count > 0 && expanded < maxNodes)
            {
                int cur = open.Pop();
                if (closed[cur] && stamp[cur] == run) continue;
                closed[cur] = true;
                expanded++;
                if (cur == goal) { found = true; break; }
                int cx = cur % W, cy = cur / W;
                float h = H0(cx, cy, goalT);
                if (h < bestH) { bestH = h; best = cur; }
                for (int k = 0; k < 8; k++)
                {
                    int nx = cx + DX[k], ny = cy + DY[k];
                    if (!Walkable(nx, ny)) continue;
                    if (k >= 4 && (!Walkable(cx + DX[k], cy) || !Walkable(cx, cy + DY[k]))) continue;
                    int ni = ny * W + nx;
                    float ng = g[cur] + COST[k];
                    if (stamp[ni] == run && (closed[ni] || g[ni] <= ng)) continue;
                    stamp[ni] = run;
                    closed[ni] = false;
                    g[ni] = ng;
                    came[ni] = cur;
                    open.Push(ni, ng + H0(nx, ny, goalT));
                }
            }
            int end = found ? goal : best;
            var cells = new List<int>();
            for (int c = end; c != -1 && c != start; c = came[c]) cells.Add(c);
            cells.Reverse();
            var pts = new List<Vector2>(cells.Count);
            foreach (var c in cells) pts.Add(Center(c % W, c / W));
            if (found && goalExact) { if (pts.Count > 0) pts[pts.Count - 1] = b; else pts.Add(b); }
            if (pts.Count == 0) return result;
            // string-pull: skip every waypoint we can see past
            var from = a;
            int i = 0;
            while (i < pts.Count)
            {
                int far = i;
                for (int j = pts.Count - 1; j > i; j--)
                    if (LineClear(from, pts[j])) { far = j; break; }
                result.Add(pts[far]);
                from = pts[far];
                i = far + 1;
            }
            return result;
        }

        static float H0(int x, int y, Vector2Int goal)
        {
            int dx = Mathf.Abs(x - goal.x), dy = Mathf.Abs(y - goal.y);
            return Mathf.Max(dx, dy) + 0.41421f * Mathf.Min(dx, dy);
        }

        class MinHeap
        {
            readonly List<int> items = new List<int>();
            readonly List<float> keys = new List<float>();
            public int Count => items.Count;

            public void Clear() { items.Clear(); keys.Clear(); }

            public void Push(int item, float key)
            {
                items.Add(item);
                keys.Add(key);
                int i = items.Count - 1;
                while (i > 0)
                {
                    int p = (i - 1) / 2;
                    if (keys[p] <= keys[i]) break;
                    Swap(i, p);
                    i = p;
                }
            }

            public int Pop()
            {
                int top = items[0];
                int last = items.Count - 1;
                items[0] = items[last];
                keys[0] = keys[last];
                items.RemoveAt(last);
                keys.RemoveAt(last);
                int i = 0;
                while (true)
                {
                    int l = i * 2 + 1, r = l + 1, m = i;
                    if (l < items.Count && keys[l] < keys[m]) m = l;
                    if (r < items.Count && keys[r] < keys[m]) m = r;
                    if (m == i) break;
                    Swap(i, m);
                    i = m;
                }
                return top;
            }

            void Swap(int a, int b)
            {
                (items[a], items[b]) = (items[b], items[a]);
                (keys[a], keys[b]) = (keys[b], keys[a]);
            }
        }
    }
}
