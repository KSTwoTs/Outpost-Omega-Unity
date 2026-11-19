using System.Collections.Generic;
using UnityEngine;

namespace Outpost.Sim
{
    /// <summary>What the battle tells the view layer about (spawns, deaths, shots).</summary>
    public interface IBattleFx
    {
        void Spawned(Entity e);
        void Died(Entity e);
        void Shot(Entity src, Vector2 from, Vector2 to, string faction, int count);
        void Impact(Vector2 at, string faction, float size, bool air);
        void ProjectileSpawned(Projectile p);
        void ProjectileEnded(Projectile p);
        void Lightning(List<Vector2> points, bool air);
        void Beam(Vector2 from, Vector2 to);
        void Message(string faction, string text);
        /// <summary>A quick ring on the ground. kind: blast / energy / stim / goo / burrow / upgrade.</summary>
        void Ring(Vector2 at, float radius, string kind);
        /// <summary>A patch that stays for a while. kind: fire / acid.</summary>
        void Zone(Vector2 at, float radius, float life, string kind);
        void Trail(Vector2 from, Vector2 to, string faction);
    }
}
