using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;
using Outpost.Content;

namespace Outpost.Sim
{
    /// <summary>
    /// Economy: income, hero respawn timers, population cap and tech level.
    /// </summary>
    public partial class Battle
    {
        void Economy(float dt)
        {
            foreach (var f in factions.Values)
            {
                float inc = 0;
                foreach (var b in buildings) if (b.alive && b.faction == f.name) inc += b.Income;
                inc += RelayIncome(f.name);
                inc += TowerIncome(f.name);
                f.income = inc * f.incomeMult;
                f.res += f.income * dt;
                TickPowerCooldowns(f, dt);
                // fallen heroes come back at the first base (the Queen only once the Swarm has Tech 3)
                if (f.hero == null && f.heroRespawn > 0)
                {
                    f.heroRespawn -= dt;
                    if (f.heroRespawn <= 0 && (f.name == "player" || f.tech >= 3))
                    {
                        f.heroRespawn = 0;
                        var back = SpawnHero(f.name);
                        if (back != null && f.name == "player") Alert(back.Pos, "Commander is back in the field", 0f, "player");
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
                foreach (var t in towers) if (t.alive && t.faction == f.name) foreach (var u in t.garrison) pop += u.pop;
                foreach (var b in buildings) if (b.alive && b.faction == f.name) foreach (var q in b.queue) pop += q.pop;
                f.popUsed = pop;
                int nb = bases.Count(b => b.Alive && b.faction == f.name);
                bool forts = bases.Any(b => b.Alive && b.faction == f.name && b.hq.upgraded);
                f.popCap = Mathf.Min(GameData.I(st, "pop_max", 60),
                    GameData.I(st, "pop_base", 25) + GameData.I(st, "pop_per_base", 10) * Mathf.Max(0, nb - 1) + (forts ? 5 : 0));
                string core = f.name == "player" ? "reactor" : "node";
                f.powerCut = TowerPowerCut(f.name);
                f.tech = 1 + Mathf.Min(2, buildings.Count(b => b.alive && b.faction == f.name && b.kind == core && b.Done));
            }
        }
    }
}
