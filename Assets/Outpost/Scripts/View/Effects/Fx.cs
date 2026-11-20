using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Outpost.Content;
using Outpost.Sim;

namespace Outpost.View
{
    /// <summary>
    /// The battle's view layer: creates / removes entity views and turns IBattleFx events into effects.
    /// Weapon looks live in WeaponEffects, ability looks in AbilityEffects, and every effect object comes from
    /// EffectPool (nothing here is created without being handed back). Call Tick(dt) to advance effects by hand
    /// (edit-mode tests); in play mode LateUpdate does it.
    /// </summary>
    public class Fx : MonoBehaviour, IBattleFx
    {
        public MapData map;
        public Transform unitsRoot;
        public readonly List<(float t, string faction, string text)> messages = new List<(float, string, string)>();

        static readonly Dictionary<Color, Material> unlit = new Dictionary<Color, Material>();
        static Mesh ringMesh;

        EffectPool pool;
        WeaponEffects weapons;
        AbilityEffects abilities;
        readonly Dictionary<Projectile, WeaponEffects.ShotView> shots = new Dictionary<Projectile, WeaponEffects.ShotView>();
        readonly List<Projectile> shotsDone = new List<Projectile>();
        readonly List<Vector3> nodes = new List<Vector3>(16);

        // status marks (stunned / slowed) on every entity the battle reported, and floating words
        class StatusMark { public FxPiece stun, stunGlow, slow; }
        readonly List<Entity> tracked = new List<Entity>();
        readonly HashSet<Entity> trackedSet = new HashSet<Entity>();
        readonly Dictionary<Entity, StatusMark> marks = new Dictionary<Entity, StatusMark>();
        struct FloatText { public Vector3 pos; public string text; public Color color; public float age, life; public int size; }
        readonly List<FloatText> floats = new List<FloatText>();
        GUIStyle floatStyle;
        float clock;
        public const int MaxFloats = 48;

        // context between Sim calls made in one go: the tower that just fired, the unit that just used its ability,
        // and the projectile that just landed (its Impact comes right after)
        Tower firing;
        Unit casting;
        string landedKind;
        Vector2 landedAt;

        public EffectPool Pool { get { Init(); return pool; } }
        public WeaponEffects Weapons { get { Init(); return weapons; } }
        public AbilityEffects Abilities { get { Init(); return abilities; } }
        public int ShotsInFlight => shots.Count;
        public int FloatCount => floats.Count;
        public int StatusMarkCount => marks.Count;
        public int TrackedCount => tracked.Count;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            unlit.Clear();      // play mode starts without a domain reload: drop materials Unity destroyed
            ringMesh = null;
        }

        void Init()
        {
            if (pool != null) return;
            pool = new EffectPool(transform);
            if (map != null) pool.groundNormal = GroundNormal;
            weapons = new WeaponEffects(pool);
            abilities = new AbilityEffects(pool, weapons);
        }

        void OnDestroy()
        {
            pool?.Dispose();
            pool = null;
            shots.Clear();
            marks.Clear();
            tracked.Clear();
            trackedSet.Clear();
            floats.Clear();
        }

        public static Material Unlit(Color c)
        {
            if (unlit.TryGetValue(c, out var m) && m != null) return m;
            m = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { name = "fx_" + ColorUtility.ToHtmlStringRGB(c) };
            m.SetColor("_BaseColor", c);
            m.SetFloat("_Cull", 0f);          // double-sided (rings, flat decals)
            unlit[c] = m;
            return m;
        }

        /// <summary>A flat selection ring lying on the ground (radius in world units). Owned by the caller.</summary>
        public static Transform MakeRing(Transform parent, float radius, Color c)
        {
            if (ringMesh == null)
            {
                const int seg = 48;
                var v = new Vector3[seg * 2];
                var tri = new int[seg * 6];
                for (int i = 0; i < seg; i++)
                {
                    float a = i / (float)seg * Mathf.PI * 2f;
                    var d = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                    v[i * 2] = d * 0.86f;
                    v[i * 2 + 1] = d;
                    int j = (i + 1) % seg;
                    tri[i * 6] = i * 2; tri[i * 6 + 1] = j * 2; tri[i * 6 + 2] = i * 2 + 1;
                    tri[i * 6 + 3] = i * 2 + 1; tri[i * 6 + 4] = j * 2; tri[i * 6 + 5] = j * 2 + 1;
                }
                ringMesh = new Mesh { name = "ring", vertices = v, triangles = tri };
                ringMesh.RecalculateNormals();
            }
            var go = new GameObject("ring");
            go.transform.SetParent(parent, false);
            go.transform.localScale = Vector3.one * radius;
            go.AddComponent<MeshFilter>().sharedMesh = ringMesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = Unlit(c);
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            return go.transform;
        }

        // ------------------------------------------------------------------ positions
        /// <summary>Ground point under a game-pixel position (flat y = 0 when there is no map, e.g. edit-mode tests).</summary>
        public Vector3 Ground(Vector2 px) => map != null ? MapBuilder.Ground(map, px.x, px.y)
            : new Vector3(px.x * MapBuilder.Unit, 0f, -px.y * MapBuilder.Unit);

        Vector3 W(Vector2 px, float lift = 0.9f) => Ground(px) + Vector3.up * lift;

        Vector3 Body(Vector2 px, bool air) => W(px, air ? 5.8f : 0.8f);

        Vector3 Muzzle(Entity e)
        {
            if (e == null) return Vector3.zero;
            bool tower = e is Tower;
            var p = W(e.Pos, tower ? 1.6f : e.flying ? 5.8f : 0.8f);
            if (e.view is EntityView v && v != null)
                p.y = v.transform.position.y + Mathf.Min(tower ? 2.4f : 1.2f, v.topHeight * (tower ? 0.7f : 0.55f));
            return p;
        }

        static bool TargetFlying(Entity src)
        {
            if (src is Unit u) return u.target != null && u.target.flying;
            if (src is Tower t) return t.target != null && t.target.flying;
            return false;
        }

        // ------------------------------------------------------------------ IBattleFx
        public void Spawned(Entity e)
        {
            EntityView.Create(e, map, unitsRoot);
            TrackStatus(e);
        }

        /// <summary>Watches an entity for stun / slow marks until it dies (Spawned does this for everything).</summary>
        public void TrackStatus(Entity e)
        {
            if (e != null && trackedSet.Add(e)) tracked.Add(e);
        }

        /// <summary>Slope under a point, from the height field sampled r units around it.</summary>
        Vector3 GroundNormal(Vector3 at, float r)
        {
            if (map == null) return Vector3.up;
            r = Mathf.Clamp(r, 0.5f, 6f);
            float px = at.x / MapBuilder.Unit, py = -at.z / MapBuilder.Unit, rp = r / MapBuilder.Unit;
            var xa = MapBuilder.Ground(map, px - rp, py);
            var xb = MapBuilder.Ground(map, px + rp, py);
            var za = MapBuilder.Ground(map, px, py + rp);   // game y down = Unity -z
            var zb = MapBuilder.Ground(map, px, py - rp);
            var n = Vector3.Cross(zb - za, xb - xa).normalized;
            if (n.y < 0) n = -n;
            return n.y > 0.2f ? n : Vector3.up;
        }

        public void Died(Entity e)
        {
            Init();
            var at = Body(e.Pos, e.flying);
            if (e.view is EntityView v && v != null)
            {
                at = v.transform.position + Vector3.up * Mathf.Min(1.2f, v.topHeight * 0.5f);
                v.Die();
            }
            weapons.Death(e, at, Ground(e.Pos));
        }

        public void Shot(Entity src, Vector2 from, Vector2 to, string faction, int count)
        {
            Init();
            var a = src != null ? Muzzle(src) : W(from, 1f);
            var b = Body(to, TargetFlying(src));
            if (src is Tower t) weapons.TowerShot(t, a, b, count);
            else if (src is Unit u)
            {
                var d = b - a;
                d.y = 0;
                if (d.sqrMagnitude > 1e-4f) a += d.normalized * Mathf.Min(1.2f, u.radius * MapBuilder.Unit * 0.7f);
                weapons.UnitShot(u, a, b, count);
            }
            else weapons.Tracer(a, b, faction == "player" ? FxColors.PlayerTracer : FxColors.AlienTracer, 0.13f, 0.07f);
        }

        public void Impact(Vector2 at, string faction, float size, bool air)
        {
            Init();
            string hint = null;
            if (landedKind != null && (landedAt - at).sqrMagnitude < 4f) hint = landedKind;
            else if (firing != null) hint = firing.behavior;
            landedKind = null;
            weapons.Impact(Body(at, air), Ground(at), faction, size, air, hint);
        }

        public void ProjectileSpawned(Projectile p)
        {
            Init();
            if (shots.ContainsKey(p)) return;
            shots[p] = weapons.ProjectileStart(p, p.src != null ? Muzzle(p.src) : W(p.pos, 1f));
        }

        public void ProjectileEnded(Projectile p)
        {
            if (shots.TryGetValue(p, out var v))
            {
                weapons?.ProjectileEnd(v);
                shots.Remove(p);
            }
            // tower lobs land as the tower's own look (mortar / mines); everything else by projectile kind
            landedKind = p.kind == "lob" && p.src is Tower t ? t.behavior : p.kind;
            landedAt = p.dest;
        }

        public void Lightning(List<Vector2> points, bool air)
        {
            Init();
            nodes.Clear();
            for (int i = 0; i < points.Count; i++)
                nodes.Add(i == 0 ? (firing != null ? Muzzle(firing) + Vector3.up * 0.5f : W(points[0], 2.2f)) : Body(points[i], air));
            var c = firing != null && firing.behavior == "lightning" ? FxColors.Energy : FxColors.Cyan;
            weapons.Lightning(nodes, c);
        }

        public void Beam(Vector2 from, Vector2 to)
        {
            Init();
            var t = firing != null && (firing.Pos - from).sqrMagnitude < 1f ? firing : null;
            var a = t != null ? Muzzle(t) : W(from, 1.6f);
            bool air = t != null && t.target != null && t.target.flying && t.behavior != "pierce";
            weapons.Beam(t, a, Body(to, air));
        }

        public void Message(string faction, string text) => messages.Add((Time.time, faction, text));

        /// <summary>A quick ring on the ground. Rings that belong to an ability or tower just reported are drawn by them.</summary>
        public void Ring(Vector2 at, float radius, string kind)
        {
            Init();
            if (casting != null && (casting.Pos - at).sqrMagnitude < 1f)
            {
                casting = null;     // AbilityEffects already drew this ability
                return;
            }
            if (firing != null)
            {
                if (firing.behavior == "shockwave" && (firing.Pos - at).sqrMagnitude < 1f) return;   // drawn in TowerFired
                if (firing.behavior == "chrono" || firing.behavior == "hacker")
                {
                    abilities.Ring(Ground(at), radius * MapBuilder.Unit, firing.behavior == "hacker" ? "hack" : "stasis");
                    return;
                }
            }
            abilities.Ring(Ground(at), Mathf.Max(0.6f, radius * MapBuilder.Unit), kind);
        }

        /// <summary>A patch that stays for a while: fire / acid, or a Sensor Array scan.</summary>
        public void Zone(Vector2 at, float radius, float life, string kind)
        {
            Init();
            if (kind == "scan") abilities.Scan(Ground(at), radius * MapBuilder.Unit, life);
            else weapons.Zone(Ground(at), radius * MapBuilder.Unit, life, kind);
        }

        public void Trail(Vector2 from, Vector2 to, string faction)
        {
            Init();
            if (casting != null && (casting.Pos - from).sqrMagnitude < 1f) return;    // napalm streak: drawn by AbilityEffects
            weapons.StrikeTrail(W(from, 1.4f), W(to, 0.6f), faction);
        }

        public void TowerFired(Tower t, Entity target)
        {
            Init();
            firing = t;
            var tp = target != null ? Body(target.Pos, target.flying) : W(t.Pos, 0.8f);
            weapons.TowerFired(t, Muzzle(t), tp, Ground(t.Pos));
        }

        public void Ability(Unit u, string id, Vector2 at)
        {
            Init();
            casting = u;
            float r = u.ability != null ? GameData.F(u.ability, "radius") * MapBuilder.Unit : 0f;
            abilities.Use(u, id, Muzzle(u), Ground(u.Pos), Ground(at), r);
        }

        /// <summary>A leader or alien power (or the landing of an Orbital Strike / sniper shot): AbilityEffects.Power.</summary>
        public void Power(string faction, string id, Vector2 at, float radius)
        {
            Init();
            abilities.Power(id, faction, Ground(at), radius * MapBuilder.Unit);
        }

        /// <summary>Words that float up and fade (Python floating_text / number): RANK UP, Bio-Lab +16, crates.</summary>
        public void Float(Vector2 at, string text, string kind)
        {
            if (string.IsNullOrEmpty(text)) return;
            if (floats.Count >= MaxFloats) floats.RemoveAt(0);
            floats.Add(new FloatText
            {
                pos = W(at, kind == "rank" ? 3.4f : 2.4f),
                text = text,
                color = FxColors.Energy,
                life = kind == "number" ? 1.0f : 1.6f,
                size = kind == "supplies" ? 24 : kind == "rank" ? 22 : 18,
            });
        }

        // ------------------------------------------------------------------ helpers for the view and input layers
        /// <summary>A glow that fades (generic flash).</summary>
        public void Flash(Vector3 at, Color c, float size, float life)
        {
            Init();
            pool.Glow(at, FxColors.Hot(c, 1.3f), size, size * 1.2f, life);
        }

        /// <summary>A ring that shrinks away (move orders, rally points). radius in world units.</summary>
        public void Marker(Vector3 at, Color c, float radius)
        {
            Init();
            var p = pool.Flat(FxTex.Ring, FxBlend.Add, at + Vector3.up * 0.15f, FxColors.Hot(c, 1.2f), radius * 2.3f, radius * 0.6f, 0.45f);
            p.ease = 1f;
            p.angle = 0f;
        }

        /// <summary>Advances every effect by dt: projectiles follow the Sim, pieces age, the pool takes finished ones back.</summary>
        public void Tick(float dt)
        {
            Init();
            firing = null;
            casting = null;
            landedKind = null;
            shotsDone.Clear();
            foreach (var kv in shots)
            {
                var p = kv.Key;
                var v = kv.Value;
                if (v.head == null || v.age > 30f) { shotsDone.Add(p); continue; }     // lost track of it: give it back
                Vector3 pos;
                if (p.delay > 0 && p.src != null) pos = Muzzle(p.src);
                else if (p.lob)
                {
                    float f = Mathf.Clamp01(p.t / Mathf.Max(0.01f, p.total));
                    var a = p.src != null ? Muzzle(p.src) : W(p.pos, 1f);
                    var b = W(p.dest, 0.5f);
                    pos = Vector3.Lerp(a, b, f) + Vector3.up * Mathf.Sin(f * Mathf.PI) * 9f;
                }
                else pos = Body(p.pos, p.target != null && p.target.flying && (p.dest - p.pos).magnitude < 120) + Vector3.up * 0.3f;
                weapons.ProjectileMove(v, pos, dt);
            }
            foreach (var p in shotsDone)
            {
                weapons.ProjectileEnd(shots[p]);
                shots.Remove(p);
            }
            clock += dt;
            UpdateStatus();
            for (int i = floats.Count - 1; i >= 0; i--)
            {
                var f = floats[i];
                f.age += dt;
                if (f.age >= f.life) floats.RemoveAt(i);
                else floats[i] = f;
            }
            weapons.Tick(dt);
            pool.Tick(dt);
        }

        /// <summary>A spinning yellow star over stunned units and towers (sonic, chrono, hacker, stomp), a pale blue
        /// ring under slowed ones (cryo, gravity, tar, magnet). Held pieces, one set per entity, handed back when the
        /// status ends, the entity dies or it is hidden.</summary>
        void UpdateStatus()
        {
            for (int i = tracked.Count - 1; i >= 0; i--)
            {
                var e = tracked[i];
                bool gone = !e.alive;
                var v = e.view as EntityView;
                bool hasView = v != null;
                bool hidden = gone || (hasView && v.Dying) || (EntityView.IsHidden != null && EntityView.IsHidden(e)) ||
                              (e is Unit bu && bu.inBunker != null);
                bool stun = !hidden && e.stunT > 0.05f;
                bool slow = !hidden && e.slowT > 0.05f && e.slow >= 0.2f;
                marks.TryGetValue(e, out var m);
                if (m != null || stun || slow)
                {
                    if (m == null) marks[e] = m = new StatusMark();
                    var at = hasView ? v.transform.position : Body(e.Pos, e.flying) - Vector3.up * 0.8f;
                    float r = Mathf.Max(0.8f, e.radius * MapBuilder.Unit);
                    float head = hasView ? v.topHeight + 0.9f : 2.6f;
                    if (stun)
                    {
                        if (m.stun == null)
                        {
                            m.stun = pool.Quad(FxFacing.Billboard, FxTex.Star, FxBlend.Add, at, FxColors.Hot(FxColors.Energy, 1.6f), r * 1.7f, r * 1.7f, 1f);
                            m.stun.held = true;
                            m.stun.spin = 220f;
                            m.stunGlow = pool.Glow(at, FxColors.A(FxColors.Energy, 0.6f), r * 1.8f, r * 1.8f, 1f);
                            m.stunGlow.held = true;
                        }
                        m.stun.pos = m.stunGlow.pos = at + Vector3.up * head;
                    }
                    else if (m.stun != null)
                    {
                        pool.Release(m.stun);
                        pool.Release(m.stunGlow);
                        m.stun = m.stunGlow = null;
                    }
                    if (slow)
                    {
                        if (m.slow == null)
                        {
                            m.slow = pool.Flat(FxTex.Ring, FxBlend.Add, at, FxColors.Hot(FxColors.Cryo, 1.2f), r * 2.8f, r * 2.8f, 1f);
                            m.slow.held = true;
                            m.slow.spin = 40f;
                        }
                        m.slow.pos = at + Vector3.up * 0.2f;
                        m.slow.color.a = 0.55f + 0.3f * Mathf.Sin(clock * 6f + i);
                    }
                    else if (m.slow != null)
                    {
                        pool.Release(m.slow);
                        m.slow = null;
                    }
                    if (m.stun == null && m.slow == null) marks.Remove(e);
                }
                if (gone)
                {
                    tracked[i] = tracked[tracked.Count - 1];
                    tracked.RemoveAt(tracked.Count - 1);
                    trackedSet.Remove(e);
                }
            }
        }

        void OnGUI()
        {
            if (floats.Count == 0 || Event.current.type != EventType.Repaint) return;
            var cam = Camera.main;
            if (cam == null) return;
            if (floatStyle == null)
                floatStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            float scale = Screen.height / 1080f;
            var old = GUI.color;
            foreach (var f in floats)
            {
                float k = f.age / f.life;
                var sp = cam.WorldToScreenPoint(f.pos + Vector3.up * (2.5f * k));
                if (sp.z <= 0) continue;
                float a = f.age < 0.12f ? f.age / 0.12f : k > 0.6f ? (1f - k) / 0.4f : 1f;
                floatStyle.fontSize = Mathf.Max(10, Mathf.RoundToInt(f.size * scale));
                var r = new Rect(sp.x - 150, Screen.height - sp.y - 20, 300, 40);
                GUI.color = new Color(0f, 0f, 0f, a * 0.8f);
                GUI.Label(new Rect(r.x + 2, r.y + 2, r.width, r.height), f.text, floatStyle);
                GUI.color = new Color(f.color.r, f.color.g, f.color.b, a);
                GUI.Label(r, f.text, floatStyle);
            }
            GUI.color = old;
        }

        void LateUpdate() => Tick(Time.deltaTime);
    }
}
