using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;
using Outpost.Content;

namespace Outpost.Sim
{
    /// <summary>Anything on the battlefield with hit points. Positions are game pixels (x right, y down).</summary>
    public abstract class Entity
    {
        static int nextId;
        public readonly int id = ++nextId;
        public string faction, kind, name, cls = "bld";
        public float x, y, hp, maxHp, armor, radius = 15f, sight = 260f;
        public bool alive = true, flying;
        public float lastHitT = 99f;
        public float slow, slowT, stunT;        // slow is 0..1; stunned entities do nothing
        public float shield;                    // soaks damage before hit points (Shield Drones, Shield Generator)
        public JObject data;
        public object view;                     // the EntityView showing this (set by the view layer)

        public virtual bool IsUnit => false;
        public virtual bool IsBuilding => false;
        public virtual bool IsTower => false;
        public virtual bool Done => true;
        public float Ratio => maxHp > 0 ? Mathf.Clamp01(hp / maxHp) : 0f;
        public Vector2 Pos => new Vector2(x, y);

        public void ApplySlow(float amount, float time)
        {
            if (amount >= slow || slowT <= 0) slow = amount;
            slowT = Mathf.Max(slowT, time);
        }

        public void TickStatus(float dt)
        {
            lastHitT += dt;
            if (slowT > 0)
            {
                slowT -= dt;
                if (slowT <= 0) slow = 0;
            }
            if (stunT > 0) stunT -= dt;
        }
    }
}
