using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;
using Outpost.Content;

namespace Outpost.Sim
{
    public enum Order { None, Move, Attack, Hold }

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

    /// <summary>A Ram / Charge / Jetpack dash in progress.</summary>
    public class Dash
    {
        public Vector2 to;
        public float speed, radius, damage, t;
        public bool fly;
        public readonly HashSet<Entity> hit = new HashSet<Entity>();
    }

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

    public class QueueItem
    {
        public string type = "unit";     // "unit", "upgrade" or "research"
        public string id;
        public float t, total;
        public int cost, pop;
    }

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

    /// <summary>A loadout tower on a base socket (towers.json, scaled by rts.json "towers"), or an alien Spine.</summary>
    public class Tower : Entity
    {
        public override bool IsTower => true;
        public Base baseRef;
        public int socket;
        public string behavior;
        public float range, damage, rate, splash, chainRange, projectileSpeed;
        public int chain;
        public bool hitsAir;
        public float cooldown, angle, buildTotal, buildLeft;
        public Entity target;
        public override bool Done => buildLeft <= 0;

        public Tower(string kind, string faction, float x, float y, Base b, int socket)
        {
            this.kind = kind;
            this.faction = faction;
            this.x = x;
            this.y = y;
            baseRef = b;
            this.socket = socket;
            radius = 18;
            var scale = GameData.Rts["towers"];
            if (kind == "spine")
            {
                data = (JObject)GameData.Rts["alien_tower"];
                name = GameData.S(data, "name", "Spine Tower");
                behavior = "projectile";
                maxHp = GameData.F(data, "hp", 750);
                armor = GameData.F(data, "armor", 2);
                range = GameData.F(data, "range", 210);
                damage = GameData.F(data, "damage", 14);
                rate = GameData.F(data, "rate", 1.4f);
                hitsAir = GameData.B(data, "air", true);
                buildTotal = GameData.F(data, "time", 15);
                projectileSpeed = 360;
            }
            else
            {
                data = GameData.Tower(kind);
                name = GameData.S(data, "name", kind);
                behavior = GameData.S(data, "behavior", "projectile");
                var tier = data["tiers"]?[0];
                maxHp = GameData.F(data, "hp", 100) * GameData.F(scale, "hp_mult", 7);
                armor = 2;
                range = GameData.F(tier, "range", 150) * GameData.F(scale, "range_mult", 1.25f);
                damage = GameData.F(tier, "damage", 10) * GameData.F(scale, "damage_mult", 1.6f);
                rate = GameData.F(tier, "fire_rate", 1);
                splash = GameData.F(tier, "splash");
                chain = GameData.I(tier, "chain");
                chainRange = GameData.F(tier, "chain_range", 80);
                projectileSpeed = GameData.F(tier, "projectile_speed", 480);
                hitsAir = GameData.B(data, "can_target_air", true);
                buildTotal = GameData.F(scale, "build_time", 15);
            }
            hp = maxHp * 0.1f;
            buildLeft = buildTotal;
            sight = Mathf.Max(220, range + 60);
        }

        public bool CanHit(Entity e) => !e.flying || hitsAir;
    }

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

    public class Faction
    {
        public string name;
        public float res, incomeMult = 1f, income;
        public int popUsed, popCap = 25, tech = 1, kills, losses;
        public Unit hero;                // Commander / Queen while alive
        public float heroRespawn;        // seconds until the hero comes back (0 = not waiting)
        public readonly HashSet<string> research = new HashSet<string>();
        public readonly HashSet<string> researching = new HashSet<string>();
        public string ResName => name == "player" ? "Supplies" : "Biomass";
    }

    public class Projectile
    {
        public string kind;
        public Vector2 pos, dest;
        public Entity target;
        public float speed, dmg, splash, arc, t, total;
        public string cls, faction;
        public Entity src;
        public bool hitsAir, lob, pierce;
        public JObject bonus;
        public object view;
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
    }

    /// <summary>A burning / acid patch on the ground that hurts enemies standing in it.</summary>
    public class Zone
    {
        public Vector2 pos;
        public float r, dps, t, tick;
        public string faction, kind;
    }
}
