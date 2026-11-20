// AlienAI.Powers.cs · roadmap step M1.6 (filled Nov 20).
// When the alien AI casts Acid Rain, Spore Storm and Swarm Call (port of ai.py cast_powers / clusters).

using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Outpost.Sim
{
    public partial class AlienAI
    {
        float powerT;                    // seconds (thinks) until the next try
        float powerStart;                // no powers before first attack + 120 s (or passive_until)

        /// <summary>Spots where the player has a clump of units: (count, x, y), biggest first (the AI sees the whole map).</summary>
        List<(int n, float x, float y)> Clusters(int minCount = 4, float radius = 170f)
        {
            var foes = battle.units.Where(u => u.alive && u.faction != "alien" && u.faction != "wild" && u.pop > 0).ToList();
            var best = new List<(int n, float x, float y)>();
            float r2 = radius * radius;
            foreach (var u in foes.Take(40))
            {
                int n = foes.Count(o => (o.Pos - u.Pos).sqrMagnitude < r2);
                if (n >= minCount) best.Add((n, u.x, u.y));
            }
            return best.OrderByDescending(s => s.n).ThenByDescending(s => s.x).ThenByDescending(s => s.y).ToList();
        }

        /// <summary>
        /// Once a second: aim at the biggest clump of player units that is near one of its bases (600) or near its
        /// army (500). Acid Rain, else Spore Storm (then waits 25 s); Swarm Call only near its bases (then 12 s).
        /// </summary>
        void CastPowers(List<Base> bases, List<Unit> army)
        {
            powerT -= 1f;
            if (powerT > 0 || battle.time < powerStart) return;
            var spots = Clusters();
            if (spots.Count == 0) return;
            var at = new Vector2(spots[0].x, spots[0].y);
            bool nearBase = bases.Any(b => (new Vector2(b.x, b.y) - at).sqrMagnitude < 600f * 600f);
            bool nearArmy = army.Any(u => (u.Pos - at).sqrMagnitude < 500f * 500f);
            if (!nearBase && !nearArmy) return;
            foreach (var pid in new[] { "acid_rain", "spore_storm" })
                if (battle.AlienPower(pid, at))
                {
                    powerT = 25;
                    return;
                }
            if (nearBase && battle.AlienPower("swarm_call", at)) powerT = 12;
        }
    }
}
