using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;
using Outpost.Content;

namespace Outpost.Sim
{
    /// <summary>A shot in flight: a straight projectile following its target, or a lobbed shell to a point.</summary>
    public class Projectile
    {
        public string kind;
        public Vector2 pos, dest;
        public Entity target;
        public float speed, dmg, splash, arc, t, total;
        public float delay;                     // seconds before it leaves (missile / flak bursts)
        public string cls, faction;
        public Entity src;
        public bool hitsAir, lob, pierce;
        public JObject bonus;
        public object view;
    }

    /// <summary>A spot the player's Sensor Array is scanning: reveals cloaked units there for a while.</summary>
    public class ScanArea
    {
        public Vector2 pos;
        public float r, t;
    }

    /// <summary>A delayed area hit (Missile Salvo, Barrage).</summary>
    public class Strike
    {
        public float t, r, dmg;
        public Vector2 pos, from;
        public string faction, cls;
        public bool air;
        public string kind;                     // null (unit abilities), orbital, shell (Carpet Bombing), acid (Acid Rain)
        public bool burn;                       // Orbital Strike leaves fire (ignis)
    }

    /// <summary>A burning / acid patch on the ground that hurts enemies standing in it.</summary>
    public class Zone
    {
        public Vector2 pos;
        public float r, dps, t, tick;
        public string faction, kind;
    }
}
