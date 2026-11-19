using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;
using Outpost.Content;

namespace Outpost.Sim
{
    /// <summary>One entry in a building's queue: a unit, an upgrade or a research.</summary>
    public class QueueItem
    {
        public string type = "unit";     // "unit", "upgrade" or "research"
        public string id;
        public float t, total;
        public int cost, pop;
    }

    /// <summary>A building in a base slot, or the HQ / Hive at the base centre (slot -1).</summary>
    public class Building : Entity
    {
        public override bool IsBuilding => true;
        public Base baseRef;
        public int slot;                 // -1 = the HQ / Hive at the base centre
        public float buildTotal, buildLeft;
        public readonly List<QueueItem> queue = new List<QueueItem>();
        public Vector2? rally;
        public string[] trains;
        public bool upgraded;            // Fortress / Elder Hive, Heavy Supply Pad / Deep Pool
        public float scanCd;             // Sensor Array: seconds until it can Scan again
        public JObject Research => data?["research"] as JObject;
        public bool IsHq => slot < 0;
        public override bool Done => buildLeft <= 0;
        public JToken Upgrade => data?["upgrade"];
        public bool Upgrading => queue.Exists(q => q.type == "upgrade");
        public float Income => !Done ? 0f : GameData.F(data, "income") + (upgraded ? GameData.F(Upgrade, "income") : 0f);

        public Building(string kind, string faction, float x, float y, Base b, int slot)
        {
            this.kind = kind;
            this.faction = faction;
            this.x = x;
            this.y = y;
            baseRef = b;
            this.slot = slot;
            data = GameData.Building(kind);
            name = GameData.S(data, "name", kind);
            maxHp = GameData.F(data, "hp", 1000);
            hp = maxHp * 0.1f;
            armor = GameData.F(data, "armor", 2);
            radius = slot < 0 ? 50 : 30;
            sight = GameData.F(data, "sight", 260);
            buildTotal = buildLeft = GameData.F(data, "time", 20);
            trains = GameData.List(data, "trains");
        }
    }
}
