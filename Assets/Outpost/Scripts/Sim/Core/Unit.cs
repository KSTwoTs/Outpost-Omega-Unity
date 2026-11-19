using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;
using Outpost.Content;

namespace Outpost.Sim
{
    /// <summary>A unit's standing order.</summary>
    public enum Order { None, Move, Attack, Hold, Garrison }

    /// <summary>A Ram / Charge / Jetpack dash in progress.</summary>
    public class Dash
    {
        public Vector2 to;
        public float speed, radius, damage, t;
        public bool fly;
        public readonly HashSet<Entity> hit = new HashSet<Entity>();
    }

    /// <summary>A unit or squad (unit data from rts.json): stats, its order and path, abilities and buffs, support and stealth.</summary>
    public class Unit : Entity
    {
        public override bool IsUnit => true;
        public int squad, pop, kills, rank;
        public float memberHp, speed, range, minRange, damage, rate, splash;
        public bool hitsAir, hitsGround = true;
        public string projectile;
        public float cooldown, fireT, angle;
        public Order order;
        public bool attackMove, moving;
        public List<Vector2> path = new List<Vector2>();
        public Vector2? goal;
        public Entity target;
        public Vector2 guard;
        public Tower inBunker;                  // the Bunker this squad sits in (it is out of the units list meanwhile)
        public float retarget, repath, stuckT;
        public Vector2 lastPos;

        // abilities and buffs
        public JObject ability, bonus;          // bonus: extra damage multiplier per target class
        public float abilityCd, lifesteal, lifetime = -1f;
        public bool burn;
        public readonly Dictionary<string, float> buffs = new Dictionary<string, float>();   // name -> seconds left
        public Dash dash;
        public Vector2? burrowTo;
        public Vector3? leash;                  // wild nest guards: (x, y, radius) they stay inside
        public string model;                    // a different model than the unit's own (wild nests)

        // support and stealth
        public bool stealth;                    // permanently cloaked (Cloakers)
        public float detect, auraT;             // radius in which this unit reveals cloaked enemies
        public JObject heal, shieldAura;

        public bool Has(string buff) => buffs.ContainsKey(buff);

        /// <summary>Current speed / fire rate / range with slows and buffs (speed, rate, range are the base values).</summary>
        public float Speed
        {
            get
            {
                if (Has("lockdown")) return 0f;
                float s = speed;
                if (slowT > 0) s *= 1f - slow;
                if (Has("stim")) s *= 1.3f;
                if (Has("frenzy")) s *= 1.5f;
                return s;
            }
        }

        public float FireRate
        {
            get
            {
                float r = rate;
                if (Has("stim") || Has("frenzy")) r *= 1.5f;
                if (Has("lockdown")) r *= 1.5f;
                return r;
            }
        }

        public float AttackRange => Has("lockdown") ? range * 1.3f : range;

        static readonly List<string> expired = new List<string>();

        public void TickBuffs(float dt)
        {
            if (abilityCd > 0) abilityCd -= dt;
            if (buffs.Count == 0) return;
            expired.Clear();
            foreach (var k in new List<string>(buffs.Keys))
            {
                buffs[k] -= dt;
                if (buffs[k] <= 0) expired.Add(k);
            }
            foreach (var k in expired) buffs.Remove(k);
        }

        static readonly Dictionary<string, float> SpriteRadius = new Dictionary<string, float>
        {
            {"rollers", 16}, {"wrecker", 25}, {"ravager", 27}, {"mantis", 27}, {"titan", 32}, {"architect", 32},
            {"juggernaut", 27}, {"artillery", 24}, {"aa", 21}, {"hauler", 22}, {"buggy", 19}, {"brutes", 20},
            {"skitters", 12}, {"commander", 16}, {"queen", 34}, {"carrier", 30}, {"mech", 22}, {"gunship", 26},
            {"tank", 26}, {"grub", 22}, {"bomber", 18},
        };

        public Unit(string kind, string faction, float x, float y, float hpMult = 1f)
        {
            this.kind = kind;
            this.faction = faction;
            this.x = x;
            this.y = y;
            data = GameData.Unit(kind);
            name = GameData.S(data, "name", kind);
            cls = GameData.S(data, "class", "inf");
            flying = cls == "air";
            squad = Mathf.Max(1, GameData.I(data, "squad", 1));
            memberHp = GameData.F(data, "hp", 50) * hpMult;
            maxHp = hp = memberHp * squad;
            ability = data?["ability"] as JObject;
            bonus = data?["bonus"] as JObject;
            lifesteal = GameData.F(data, "lifesteal");
            burn = GameData.F(data, "burn") > 0;
            lifetime = GameData.F(data, "lifetime", -1f);
            stealth = GameData.B(data, "stealth");
            detect = GameData.F(data, "detect");
            heal = data?["heal"] as JObject;
            shieldAura = data?["shield_aura"] as JObject;
            auraT = Random.Range(0f, 0.5f);
            armor = GameData.F(data, "armor");
            speed = GameData.F(data, "speed", 60);
            range = GameData.F(data, "range", 100);
            minRange = GameData.F(data, "min_range");
            damage = GameData.F(data, "damage");
            rate = GameData.F(data, "rate", 1);
            splash = GameData.F(data, "splash");
            hitsAir = GameData.B(data, "air");
            hitsGround = GameData.B(data, "ground", true);
            projectile = GameData.S(data, "projectile", "bullet");
            sight = GameData.F(data, "sight", 220);
            pop = GameData.I(data, "pop", 1);
            radius = SpriteRadius.TryGetValue(kind, out var r) ? r
                : cls == "veh" ? 18 : cls == "air" ? 16 : 15;
            cooldown = Random.Range(0f, 1f / Mathf.Max(0.1f, rate));
            retarget = Random.Range(0f, 0.3f);
            guard = lastPos = new Vector2(x, y);
        }

        /// <summary>Squad members still standing (damage takes members off one by one).</summary>
        public int Members => squad <= 1 ? 1 : Mathf.Clamp(Mathf.CeilToInt(hp / memberHp - 1e-4f), 1, squad);

        public bool CanHit(Entity e) => e.flying ? hitsAir : hitsGround;
    }
}
