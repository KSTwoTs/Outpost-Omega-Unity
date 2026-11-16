using System.Collections.Generic;
using UnityEngine;
using Outpost.Content;
using Outpost.Sim;

namespace Outpost.View
{
    /// <summary>
    /// Shows one sim entity: the exported model (one per squad member), facing, flying height,
    /// construction rise, the selection ring, and its animation: stepping and attack poses, leaning
    /// into a run, banking in turns, turrets tracking their target, spinning rotors and falling
    /// over on death. Reads the sim; never changes it.
    /// </summary>
    public class EntityView : MonoBehaviour
    {
        public Entity entity;
        MapData map;
        readonly List<Transform> members = new List<Transform>();
        readonly List<ModelRig> rigs = new List<ModelRig>();          // null where the model is missing
        Transform ring;
        float shownAngle, lastAngle, yawRate, bank, lean, stride, bob, dying;
        bool selected;
        public float topHeight = 2f;     // world units above the ground, for health bars

        /// <summary>Set by the game: is this entity invisible to the player right now (cloaked, undetected)?</summary>
        public static System.Func<Entity, bool> IsHidden;

        public static Color PlayerColor = new Color(0.35f, 0.95f, 0.55f);
        public static Color EnemyColor = new Color(1f, 0.35f, 0.3f);

        public static EntityView Create(Entity e, MapData map, Transform parent)
        {
            var go = new GameObject($"{e.faction}:{e.kind}#{e.id}");
            go.transform.SetParent(parent, false);
            var v = go.AddComponent<EntityView>();
            v.entity = e;
            v.map = map;
            e.view = v;
            v.Build();
            return v;
        }

        /// <summary>The exported model that shows this entity (also used for HUD portraits).</summary>
        public static string ModelNameFor(Entity e)
        {
            if (e is Unit mu) return mu.model ?? e.kind;
            if (e is Tower) return e.kind == "spine" ? "bld_spine" : "tur_" + e.kind;
            if (e is Building b && b.IsHq) return b.kind == "hq" ? "bld_hq2" : "bld_hive2";
            return "bld_" + e.kind;
        }

        void Build()
        {
            int n = entity is Unit u ? u.squad : 1;
            string model = ModelNameFor(entity);
            for (int i = 0; i < n; i++)
            {
                var rig = ModelRig.Create(model, transform);
                Transform m;
                if (rig != null) m = rig.root;
                else
                {
                    var stand = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                    DestroyImmediate(stand.GetComponent<Collider>());
                    stand.transform.SetParent(transform, false);
                    m = stand.transform;
                }
                rigs.Add(rig);
                members.Add(m);
            }
            transform.position = MapBuilder.Ground(map, entity.x, entity.y);
            var bounds = new Bounds(transform.position, Vector3.zero);
            foreach (var r in members[0].GetComponentsInChildren<Renderer>()) bounds.Encapsulate(r.bounds);
            topHeight = Mathf.Max(1.2f, bounds.max.y - transform.position.y);
            ring = Fx.MakeRing(transform, entity.radius * MapBuilder.Unit * 1.25f,
                entity.faction == "player" ? PlayerColor : EnemyColor);
            ring.gameObject.SetActive(false);
            shownAngle = lastAngle = (entity is Unit uu ? uu.angle : BodyAngle(entity as Tower)) * Mathf.Rad2Deg;
            bob = Random.value * 10f;
            stride = Random.value * 2f;
            Sync(1f);
        }

        /// <summary>A turret with a turning head keeps its base still; one without turns whole.</summary>
        float BodyAngle(Tower t) => t == null || (rigs.Count > 0 && rigs[0] != null && rigs[0].HasTurret) ? 0f : t.angle;

        public void SetSelected(bool on)
        {
            selected = on;
            if (ring != null) ring.gameObject.SetActive(on);
        }

        public bool Selected => selected;

        public float FlyHeight => entity.flying ? 5.5f : 0f;

        public Vector3 HeadPosition => transform.position + Vector3.up * (topHeight + 0.4f);

        void LateUpdate()
        {
            if (dying > 0)
            {
                float dt = Time.deltaTime;
                dying += dt;
                float sink = entity is Unit ? 1.2f : 2.5f;
                if (entity is Unit && !entity.flying)
                {
                    // fall over first, then sink into the ground
                    if (dying < 0.45f)
                        foreach (var m in members) m.Rotate(190f * dt, 0f, 0f, Space.Self);
                    else transform.position += Vector3.down * dt * sink;
                }
                else transform.position += Vector3.down * dt * (entity.flying ? 9f : sink);
                if (entity.flying) transform.Rotate(0, 0, 200 * dt, Space.Self);
                if (dying > (entity is Unit ? 1.5f : 3f)) Destroy(gameObject);
                return;
            }
            Sync(1f - Mathf.Exp(-12f * Time.deltaTime));
        }

        void Sync(float k)
        {
            var e = entity;
            float dt = Time.deltaTime;
            var ground = MapBuilder.Ground(map, e.x, e.y);
            var p = ground;
            bob += dt;
            if (e.flying) p.y += FlyHeight + Mathf.Sin(bob * 2f) * 0.25f;
            if (e is Unit dug && dug.Has("burrowed")) p.y -= 4f;        // tunnelling: out of sight
            transform.position = p;
            if (ring != null) ring.localPosition = new Vector3(0, ground.y - p.y + 0.12f, 0);

            float target = e is Unit u ? u.angle : BodyAngle(e as Tower);
            shownAngle = Mathf.LerpAngle(shownAngle, target * Mathf.Rad2Deg, k);
            if (e is Building) shownAngle = 0;
            var rot = Quaternion.Euler(0, shownAngle, 0);
            if (dt > 1e-5f) yawRate = Mathf.Lerp(yawRate, Mathf.DeltaAngle(lastAngle, shownAngle) / dt, k * 0.5f);
            lastAngle = shownAngle;

            if (e is Unit un) SyncUnit(un, rot, p, k, dt);
            else
            {
                var m = members[0];
                m.localRotation = rot;
                float rise = 1f;
                if (e is Building b && !b.Done) rise = Mathf.Lerp(0.15f, 1f, 1f - b.buildLeft / Mathf.Max(0.01f, b.buildTotal));
                if (e is Tower tw)
                {
                    if (!tw.Done) rise = Mathf.Lerp(0.15f, 1f, 1f - tw.buildLeft / Mathf.Max(0.01f, tw.buildTotal));
                    var rig = rigs[0];
                    if (rig != null && rig.HasTurret) rig.Aim(tw.angle * Mathf.Rad2Deg, k);
                    rig?.Spin(240f * dt);
                }
                m.localScale = new Vector3(1f, rise, 1f);
            }
        }

        void SyncUnit(Unit un, Quaternion rot, Vector3 p, float k, float dt)
        {
            bool hidden = IsHidden != null && IsHidden(un);
            int alive = un.Members;
            float ringR = 8f + un.squad * 1.8f;
            bool firing = un.fireT > 0;
            bool moving = un.moving;

            // steps per second follow the unit's speed; flyers beat their wings or pulse engines all the time
            float pace = un.flying ? 7f : Mathf.Clamp(un.Speed / 20f, 2.5f, 9f);
            if (moving || un.flying) stride += dt * pace;
            float wantLean = !moving ? 0f : un.flying ? 8f : un.cls == "inf" ? 6f : 2.5f;
            lean = Mathf.Lerp(lean, wantLean, k * 0.4f);
            float wantBank = un.flying ? Mathf.Clamp(-yawRate * 0.09f, -28f, 28f) : 0f;
            bank = Mathf.Lerp(bank, wantBank, k * 0.25f);
            var tilt = Quaternion.Euler(bank, 0f, -lean);         // the models face +x: z pitches, x rolls

            float aim = 0f;
            if (un.target != null && un.target.alive)
                aim = Mathf.DeltaAngle(shownAngle, Mathf.Atan2(un.target.y - un.y, un.target.x - un.x) * Mathf.Rad2Deg);

            for (int i = 0; i < members.Count; i++)
            {
                var m = members[i];
                bool show = i < alive && !hidden;
                if (m.gameObject.activeSelf != show) m.gameObject.SetActive(show);
                if (!show) continue;
                Vector2 off = Vector2.zero;
                if (un.squad > 1 && !(i == 0 && un.squad >= 5))
                {
                    int div = Mathf.Max(1, alive - (un.squad >= 5 ? 1 : 0));
                    int idx = un.squad >= 5 ? i - 1 : i;
                    float a = idx / (float)div * Mathf.PI * 2f + 0.4f;
                    off = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * ringR;
                }
                // offsets turn with the squad; game y-down -> unity -z
                var local = rot * new Vector3(off.x, 0, -off.y) * MapBuilder.Unit;
                float y = 0f;
                if (!un.flying)
                {
                    var wp = MapBuilder.Ground(map, un.x + local.x / MapBuilder.Unit, un.y - local.z / MapBuilder.Unit);
                    y = wp.y - p.y;
                }
                float phase = stride + i * 0.5f;                  // squad members are out of step with each other
                bool stepping = (moving || un.flying) && ((int)phase & 1) == 1;
                float recoil = firing ? -0.12f * (un.fireT / 0.16f) : 0f;
                float hop = moving && !un.flying ? Mathf.Abs(Mathf.Sin(phase * Mathf.PI)) * (un.cls == "inf" ? 0.07f : 0.03f) : 0f;
                m.localPosition = new Vector3(local.x, y + hop, local.z) + rot * new Vector3(recoil, 0, 0);
                m.localRotation = rot * tilt;

                var rig = rigs[i];
                if (rig == null) continue;
                // a turret shows the shot itself; everything else strikes its attack pose
                rig.SetPose(firing ? ModelRig.Attack : stepping ? ModelRig.Step : ModelRig.Stand);
                rig.Aim(aim, k);
                rig.Spin(1500f * dt);
            }
        }

        public void Die()
        {
            if (dying > 0) return;
            dying = 0.0001f;
            SetSelected(false);
        }

        public bool Dying => dying > 0;
    }
}
