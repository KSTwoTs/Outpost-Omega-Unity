// Leaders.cs · roadmap step M1.6 (filled Nov 20).
// The 9 leaders' passives (rts.json "leaders", the Command-mode side of data/commanders.json).

using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Outpost.Sim
{
    /// <summary>
    /// The player's leader and its passive, as sim.py applies it: Commander HP / damage (vanguard), cheaper
    /// buildings, towers and expansions (engineer), faster and harder leader powers (tactician), unit and class
    /// damage / range (ignis, kestrel, hawk), cheaper air units (kestrel), slowed aliens taking more damage and longer
    /// Cryo (rime), tougher buildings and towers (bastion), starting Supplies and income (ledger). Unit multipliers
    /// are read in UnitMods, costs in UnitCost / BuildCost / TowerCost / ExpansionCost, structure HP in FoundBase /
    /// PlaceBuilding / PlaceTower, slowed damage in Hit and the power numbers in Battle.Powers.
    /// Call SetLeader before StandardStart (the Commander's HP is set when it spawns).
    /// </summary>
    public partial class Battle
    {
        string appliedLeader;

        /// <summary>Every leader id in rts.json, in data order (vanguard first).</summary>
        public static List<string> LeaderIds()
        {
            var list = new List<string>();
            if (GameData.Rts["leaders"] is JObject all)
                foreach (var p in all.Properties())
                    if (!p.Name.StartsWith("_")) list.Add(p.Name);
            return list;
        }

        /// <summary>The player's leader entry in rts.json (null if unknown).</summary>
        public JToken LeaderData => GameData.Rts["leaders"]?[leader];

        /// <summary>A plain number from the leader passive (def when the leader does not have it).</summary>
        public float LeaderF(string key, float def = 1f) => GameData.F(LeaderData, key, def);

        public bool LeaderB(string key) => GameData.B(LeaderData, key);

        /// <summary>A per-unit or per-class multiplier table entry (unit_damage, class_damage, unit_range, class_cost).</summary>
        public float LeaderTable(string table, string key) => key == null ? 1f : GameData.F(LeaderData?[table], key, 1f);

        public string LeaderName => leader.Length > 0 ? char.ToUpper(leader[0]) + leader.Substring(1) : leader;

        public string LeaderDesc => GameData.S(LeaderData, "desc", "");

        /// <summary>
        /// Picks the player's leader: starting Supplies and income now, the four powers (Z X C shared, V the
        /// leader's own). Safe to call again; the previous leader's starting Supplies are taken back.
        /// </summary>
        public void SetLeader(string id)
        {
            if (string.IsNullOrEmpty(id) || id.StartsWith("_") || GameData.Rts["leaders"]?[id] == null) id = "vanguard";
            var P = Player;
            if (appliedLeader != null) P.res -= GameData.F(GameData.Rts["leaders"]?[appliedLeader], "start_supplies", 0f);
            leader = appliedLeader = id;
            P.res += LeaderF("start_supplies", 0f);
            P.incomeMult = LeaderF("income", 1f);
            SetupPowers();
        }

        /// <summary>Structure HP multiplier: the leader's building_hp for the player (bastion +50%), 1 otherwise.</summary>
        public float BuildingHpMult(string faction) => faction == "player" ? LeaderF("building_hp", 1f) : 1f;

        /// <summary>The Python game's int() of a cost times a multiplier (a hair of slack for float error).</summary>
        static int Cost(float cost, float mult) => (int)(cost * mult + 1e-3f);

        /// <summary>A unit's price: the player's leader may make a class cheaper (kestrel air -15%).</summary>
        public int UnitCost(string faction, string kind)
        {
            var d = GameData.Unit(kind);
            float c = GameData.F(d, "cost", 100);
            return faction == "player" ? Cost(c, LeaderTable("class_cost", GameData.S(d, "class", "inf"))) : (int)c;
        }
    }
}
