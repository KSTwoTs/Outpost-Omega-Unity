using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;
using Outpost.Content;

namespace Outpost.Sim
{
    /// <summary>One side of the battle: resources, income, population, tech, research and its hero.</summary>
    public class Faction
    {
        public string name;
        public float res, incomeMult = 1f, income;
        public float powerCut;           // Uplink towers: leader powers recharge faster (Battle.PowerCooldown)
        public readonly Dictionary<string, float> powerCd = new Dictionary<string, float>();   // power id -> seconds until ready
        public int popUsed, popCap = 25, tech = 1, kills, losses;
        public Unit hero;                // Commander / Queen while alive
        public float heroRespawn;        // seconds until the hero comes back (0 = not waiting)
        public readonly HashSet<string> research = new HashSet<string>();
        public readonly HashSet<string> researching = new HashSet<string>();
        public string ResName => name == "player" ? "Supplies" : "Biomass";
    }
}
