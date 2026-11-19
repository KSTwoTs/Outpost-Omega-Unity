using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;
using Outpost.Content;

namespace Outpost.Sim
{
    /// <summary>A main base or expansion: the HQ / Hive plus its building slots and turret sockets.</summary>
    public class Base
    {
        public string faction;
        public MapData.SiteData site;
        public Building hq;
        public Building[] slots;
        public Tower[] sockets;
        public float x => site.x;
        public float y => site.y;
        public bool Alive => hq != null && hq.alive;

        /// <summary>Slots the HQ allows: 5, or 7 once it is upgraded to a Fortress / Elder Hive.</summary>
        public int SlotLimit => Mathf.Min(slots.Length,
            hq != null && hq.upgraded ? GameData.I(hq.Upgrade, "slots", 7) : GameData.I(hq?.data, "slots", 5));
    }

    /// <summary>A relay on the map. Units standing on it capture it for their side.</summary>
    public class Relay
    {
        public float x, y;
        public string owner;             // null, "player" or "alien"
        public float progress;           // -1..1: alien <- 0 -> player
        public bool contested;
    }

    /// <summary>A supply crate lying on the map: the first unit to reach it takes its value.</summary>
    public class Crate
    {
        public float x, y;
        public int value;
        public bool alive = true;
    }
}
