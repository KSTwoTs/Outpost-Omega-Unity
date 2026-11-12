using System.Collections.Generic;
using UnityEngine;
using Outpost.Content;
using Outpost.Sim;

namespace Outpost.View
{
    /// <summary>
    /// Shows one sim entity: the exported model (one per squad member), facing, flying height,
    /// firing recoil, construction rise and the selection ring. Reads the sim; never changes it.
    /// </summary>
    public class EntityView : MonoBehaviour
    {
        public Entity entity;
        MapData map;
        readonly List<Transform> members = new List<Transform>();
        Transform ring;
        float shownAngle, bob, dying;
        bool selected;
        public float topHeight = 2f;     // world units above the ground, for health bars

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

        string ModelName()
        {
            var e = entity;
            if (e is Unit) return e.kind;
            if (e is Tower) return e.kind == "spine" ? "bld_spine" : "tur_" + e.kind;
            if (e is Building b && b.IsHq) return b.kind == "hq" ? "bld_hq2" : "bld_hive2";
            return "bld_" + e.kind;
        }

        void Build()
        {
            int n = entity is Unit u ? u.squad : 1;
            string model = ModelName();
            for (int i = 0; i < n; i++)
            {
                var m = ModelLibrary.Spawn(model, transform);
                if (m == null)
                {
                    m = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                    DestroyImmediate(m.GetComponent<Collider>());
                    m.transform.SetParent(transform, false);
                }
                members.Add(m.transform);
            }
            transform.position = MapBuilder.Ground(map, entity.x, entity.y);
            var bounds = new Bounds(transform.position, Vector3.zero);
            foreach (var r in members[0].GetComponentsInChildren<Renderer>()) bounds.Encapsulate(r.bounds);
            topHeight = Mathf.Max(1.2f, bounds.max.y - transform.position.y);
            ring = Fx.MakeRing(transform, entity.radius * MapBuilder.Unit * 1.25f,
                entity.faction == "player" ? PlayerColor : EnemyColor);
            ring.gameObject.SetActive(false);
            shownAngle = (entity is Unit uu ? uu.angle : entity is Tower t ? t.angle : 0f) * Mathf.Rad2Deg;
            bob = Random.value * 10f;
            Sync(1f);
        }

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
                dying += Time.deltaTime;
                float sink = entity is Unit ? 1.2f : 2.5f;
                transform.position += Vector3.down * Time.deltaTime * (entity.flying ? 9f : sink);
                if (entity.flying) transform.Rotate(0, 0, 200 * Time.deltaTime, Space.Self);
                if (dying > (entity is Unit ? 1.2f : 3f)) Destroy(gameObject);
                return;
            }
            Sync(1f - Mathf.Exp(-12f * Time.deltaTime));
        }

        void Sync(float k)
        {
            var e = entity;
            var ground = MapBuilder.Ground(map, e.x, e.y);
            var p = ground;
            bob += Time.deltaTime;
            if (e.flying) p.y += FlyHeight + Mathf.Sin(bob * 2f) * 0.25f;
            if (e is Unit dug && dug.Has("burrowed")) p.y -= 4f;        // tunnelling: out of sight
            transform.position = p;
            if (ring != null) ring.localPosition = new Vector3(0, ground.y - p.y + 0.12f, 0);

            float target = e is Unit u ? u.angle : e is Tower t ? t.angle : 0f;
            shownAngle = Mathf.LerpAngle(shownAngle, target * Mathf.Rad2Deg, k);
            if (e is Building) shownAngle = 0;
            var rot = Quaternion.Euler(0, shownAngle, 0);

            if (e is Unit un)
            {
                int alive = un.Members;
                float ringR = 8f + un.squad * 1.8f;
                for (int i = 0; i < members.Count; i++)
                {
                    var m = members[i];
                    bool show = i < alive;
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
                    if (!e.flying)
                    {
                        var wp = MapBuilder.Ground(map, e.x + local.x / MapBuilder.Unit, e.y - local.z / MapBuilder.Unit);
                        y = wp.y - p.y;
                    }
                    float recoil = un.fireT > 0 ? -0.18f * (un.fireT / 0.16f) : 0f;
                    float step = un.moving && !e.flying ? Mathf.Abs(Mathf.Sin((bob + i) * 9f)) * 0.08f : 0f;
                    m.localPosition = new Vector3(local.x, y + step, local.z) + rot * new Vector3(recoil, 0, 0);
                    m.localRotation = rot;
                }
            }
            else
            {
                var m = members[0];
                m.localRotation = rot;
                float rise = 1f;
                if (e is Building b && !b.Done) rise = Mathf.Lerp(0.15f, 1f, 1f - b.buildLeft / Mathf.Max(0.01f, b.buildTotal));
                if (e is Tower tw && !tw.Done) rise = Mathf.Lerp(0.15f, 1f, 1f - tw.buildLeft / Mathf.Max(0.01f, tw.buildTotal));
                m.localScale = new Vector3(1f, rise, 1f);
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
