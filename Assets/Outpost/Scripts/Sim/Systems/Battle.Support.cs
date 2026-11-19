using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;
using Outpost.Content;

namespace Outpost.Sim
{
    /// <summary>
    /// Support: healer and Shield Drone auras on units, Shield Generators on buildings and towers.
    /// </summary>
    public partial class Battle
    {
        // ------------------------------------------------------------------ support
        /// <summary>Healers (Medics, Menders, Sparrow) and Shield Drones; ticks every 0.5 s per unit.</summary>
        void UnitAuras(Unit u)
        {
            if (u.heal != null)
            {
                float radius = GameData.F(u.heal, "radius", 130);
                float amt = GameData.F(u.heal, "rate", 9) * 0.5f * (1 + 0.1f * u.rank);
                var classes = GameData.List(u.heal, "classes");
                foreach (var e in Near(u.x, u.y, radius))
                {
                    if (e == u || !e.alive || e.faction != u.faction || e.hp >= e.maxHp || !e.Done) continue;
                    if (System.Array.IndexOf(classes, e.cls) < 0) continue;
                    if (D2(u.x, u.y, e.x, e.y) > (radius + e.radius) * (radius + e.radius)) continue;
                    e.hp = Mathf.Min(e.maxHp, e.hp + amt * (e.cls == "bld" ? 3 : 1));
                    u.fireT = 0.3f;
                }
            }
            if (u.shieldAura != null)
            {
                float radius = GameData.F(u.shieldAura, "radius", 130), cap = GameData.F(u.shieldAura, "cap", 70);
                float add = GameData.F(u.shieldAura, "rate", 12) * 0.5f;
                foreach (var e in Near(u.x, u.y, radius))
                    if (e != u && e.alive && e is Unit && e.faction == u.faction && e.shield < cap &&
                        D2(u.x, u.y, e.x, e.y) <= (radius + e.radius) * (radius + e.radius))
                        e.shield = Mathf.Min(cap, e.shield + add);
            }
        }

        /// <summary>Shield Generators keep a shield on every building and tower around them; every 0.5 s.</summary>
        void UpdateSupport()
        {
            UpdateTowerBuffs();
            foreach (var g in buildings)
            {
                var sh = g.data?["shield"];
                if (sh == null || !g.alive || !g.Done) continue;
                float r = GameData.F(sh, "radius", 360), frac = GameData.F(sh, "frac", 0.3f), add = GameData.F(sh, "rate", 40) * 0.5f;
                foreach (var e in All())
                {
                    if (e.IsUnit || !e.alive || e.faction != g.faction || D2(e.x, e.y, g.x, g.y) > r * r) continue;
                    float cap = e.maxHp * frac;
                    if (e.shield < cap) e.shield = Mathf.Min(cap, e.shield + add);
                }
            }
        }
    }
}
