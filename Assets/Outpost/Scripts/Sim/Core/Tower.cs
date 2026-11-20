using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;
using Outpost.Content;

namespace Outpost.Sim
{
    /// <summary>
    /// A loadout tower on a base socket (towers.json, scaled by rts.json "towers"), an alien Spine Tower
    /// (rts.json "alien_tower") or a Bunker (rts.json "bunker"). Port of entities.py Tower: stats come from the
    /// current tier; upgrades keep the HP ratio. Behaviors are run by Battle.Towers (see TowerBehaviors).
    /// </summary>
    public class Tower : Entity
    {
        public override bool IsTower => true;
        public Base baseRef;
        public int socket;
        public string behavior;
        public TowerBehaviors.Group group;
        public int tier;
        public JObject stats;                   // the current tier's numbers (towers.json tiers[tier])
        public JArray tiers;
        public float hpMult = 1f;               // Turret Plating research
        public int costPaid;                    // everything paid so far (sell refunds 60%)
        public float lifetime = -1f;            // Turret Drop towers leave after this many seconds (-1 = stays)
        // scale (rts.json towers; 1 for Spines and Bunkers)
        public float scaleHp = 1f, scaleRange = 1f, scaleDamage = 1f, scaleCost = 2f;
        // shown / derived numbers for the current tier
        public float range, damage, rate, splash, chainRange, minRange;
        public int chain;
        public bool hitsAir;
        public float cooldown = 0.5f, retarget, angle, buildTotal, buildLeft, spin;
        public float beamT;                     // laser ramp-up time on the current target
        public float buffRate = 1f, buffDamage = 1f, buffRange = 1f;
        public Entity target;
        // Bunker
        public readonly List<Unit> garrison = new List<Unit>();
        public int capacity;

        public override bool Done => buildLeft <= 0;
        public bool IsBunker => behavior == "garrison";
        public int MaxTier => tiers != null ? tiers.Count : 1;

        public Tower(string kind, string faction, float x, float y, Base b, int socket, float hpMult = 1f)
        {
            this.kind = kind;
            this.faction = faction;
            this.x = x;
            this.y = y;
            baseRef = b;
            this.socket = socket;
            this.hpMult = hpMult;
            radius = 17;
            armor = 0;                          // Python towers have no armor
            float baseHp;
            if (kind == "spine")
            {
                data = (JObject)GameData.Rts["alien_tower"];
                name = GameData.S(data, "name", "Spine Tower");
                behavior = "projectile";
                baseHp = GameData.F(data, "hp", 750);
                hitsAir = GameData.B(data, "air", true);
                tiers = new JArray(new JObject
                {
                    ["damage"] = GameData.F(data, "damage", 14), ["range"] = GameData.F(data, "range", 210),
                    ["fire_rate"] = GameData.F(data, "rate", 1.4f),
                });
                buildTotal = GameData.F(data, "time", 15);
            }
            else if (kind == "bunker")
            {
                data = (JObject)GameData.Rts["bunker"];
                name = GameData.S(data, "name", "Bunker");
                behavior = "garrison";
                baseHp = GameData.F(data, "hp", 2200);
                hitsAir = true;
                tiers = new JArray(new JObject { ["range"] = GameData.F(data, "range", 220) });
                buildTotal = GameData.F(data, "time", 12);
                capacity = GameData.I(data, "capacity", 3);
            }
            else
            {
                data = GameData.Tower(kind);
                var scale = GameData.Rts["towers"];
                name = GameData.S(data, "name", kind);
                behavior = GameData.S(data, "behavior", "projectile");
                baseHp = GameData.F(data, "hp", 100);
                hitsAir = GameData.B(data, "can_target_air", true);
                tiers = data?["tiers"] as JArray ?? new JArray(new JObject());
                scaleHp = GameData.F(scale, "hp_mult", 7);
                scaleRange = GameData.F(scale, "range_mult", 1.25f);
                scaleDamage = GameData.F(scale, "damage_mult", 1.6f);
                scaleCost = GameData.F(scale, "cost_mult", 2f);
                buildTotal = GameData.F(scale, "build_time", 15);
            }
            group = TowerBehaviors.Of(behavior);
            this.baseHp = baseHp;
            ApplyTier();
            hp = maxHp * 0.1f;
            buildLeft = buildTotal;
            spin = Random.value * 6f;
        }

        float baseHp;

        /// <summary>Loads the current tier's numbers; HP keeps its ratio (entities.py apply_tier).</summary>
        public void ApplyTier()
        {
            float old = maxHp > 0 ? Mathf.Max(0f, hp / maxHp) : 1f;
            stats = (JObject)tiers[Mathf.Min(tier, tiers.Count - 1)];
            maxHp = GameData.F(stats, "max_hp", baseHp) * scaleHp * hpMult * (1 + 0.25f * tier);
            hp = maxHp * old;
            range = GameData.F(stats, "range") * scaleRange;
            minRange = GameData.F(stats, "min_range") * scaleRange;
            sight = Mathf.Max(220, range + 60);
            if (behavior == "support") sight = 560 + 60 * tier;
            damage = group == TowerBehaviors.Group.Attack ? TowerBehaviors.ShotDamage(behavior, stats) * scaleDamage : 0f;
            rate = TowerBehaviors.FireRate(behavior, stats);
            splash = GameData.F(stats, "splash");
            chain = GameData.I(stats, "chain");
            chainRange = GameData.F(stats, "chain_range", 90);
        }

        /// <summary>Cost of the next tier, or -1 at the top tier (entities.py upgrade_cost).</summary>
        public int UpgradeCost()
        {
            if (tiers == null || tier + 1 >= tiers.Count) return -1;
            return (int)(GameData.F(tiers[tier + 1], "upgrade_cost", 100) * scaleCost);
        }

        public bool CanHit(Entity e) => !e.flying || hitsAir;
    }
}
