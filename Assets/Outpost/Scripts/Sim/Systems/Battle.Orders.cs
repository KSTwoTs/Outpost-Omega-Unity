using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;
using Outpost.Content;

namespace Outpost.Sim
{
    /// <summary>
    /// Orders: formations, move / attack-move, attack and stop / hold.
    /// </summary>
    public partial class Battle
    {
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
    }
}
