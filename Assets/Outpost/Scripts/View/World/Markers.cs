using UnityEngine;
using Outpost.Sim;

namespace Outpost.View
{
    /// <summary>
    /// Marks drawn over the battlefield rather than in the HUD: health, shield and progress bars above
    /// units and buildings, rank stars, and the drag-select box. Drawn immediately each frame, which
    /// suits things that follow hundreds of moving objects.
    /// </summary>
    public class Markers : MonoBehaviour
    {
        public Battle battle;
        public CommandInput input;
        public Camera cam;
        public Outpost.Content.MapData map;

        GUIStyle small;

        static void Fill(Rect r, Color c)
        {
            var old = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = old;
        }

        void OnGUI()
        {
            if (battle == null || input == null || cam == null || Event.current.type != EventType.Repaint) return;
            if (small == null) small = new GUIStyle(GUI.skin.label) { fontSize = 12 };
            HealthBars();
            RelayBars();
            DragBox();
        }

        void HealthBars()
        {
            foreach (var e in battle.All())
            {
                if (!e.alive || !(e.view is EntityView v) || v == null || !battle.Seen(e, "player")) continue;
                bool show = v.Selected || input.hover == e || e.hp < e.maxHp - 0.5f || !e.Done || e.shield > 0.5f;
                if (!show) continue;
                var sp = cam.WorldToScreenPoint(v.HeadPosition);
                if (sp.z <= 0) continue;
                float w = Mathf.Clamp(e.radius * 1.4f, 26, 90);
                var r = new Rect(sp.x - w / 2, Screen.height - sp.y - 6, w, 5);
                Fill(new Rect(r.x - 1, r.y - 1, r.width + 2, r.height + 2), new Color(0, 0, 0, 0.7f));
                float k = e.Ratio;
                var c = e.faction == "player" ? Color.Lerp(new Color(1f, 0.3f, 0.2f), new Color(0.35f, 1f, 0.45f), k)
                    : Color.Lerp(new Color(1f, 0.25f, 0.2f), new Color(1f, 0.55f, 0.3f), k);
                Fill(new Rect(r.x, r.y, r.width * k, r.height), c);
                if (e.shield > 0.5f)
                    Fill(new Rect(r.x, r.y - 3, r.width * Mathf.Clamp01(e.shield / Mathf.Max(1f, e.maxHp * 0.3f)), 2), new Color(0.45f, 0.85f, 1f));
                if (e is Unit u && u.squad > 1)
                    for (int i = 1; i < u.squad; i++) Fill(new Rect(r.x + r.width * i / u.squad, r.y, 1, r.height), new Color(0, 0, 0, 0.8f));
                if (e is Unit ru && ru.rank > 0)
                    GUI.Label(new Rect(r.xMax + 2, r.y - 8, 30, 18), new string('*', ru.rank), small);
                float work = e is Building b && !b.Done ? 1 - b.buildLeft / b.buildTotal
                    : e is Tower t && !t.Done ? 1 - t.buildLeft / t.buildTotal
                    : e is Building qb && qb.queue.Count > 0 ? 1 - qb.queue[0].t / qb.queue[0].total : -1;
                if (work >= 0)
                {
                    bool building = !e.Done;
                    Fill(new Rect(r.x, r.yMax + 2, r.width, 3), new Color(0, 0, 0, 0.7f));
                    Fill(new Rect(r.x, r.yMax + 2, r.width * work, 3), building ? new Color(0.4f, 0.8f, 1f) : new Color(1f, 0.85f, 0.3f));
                }
            }
        }

        /// <summary>A capture bar over every relay that is being taken or fought over.</summary>
        void RelayBars()
        {
            if (map == null) return;
            foreach (var r in battle.relays)
            {
                float k = Mathf.Abs(r.progress);
                if ((k <= 0.001f || k >= 0.999f) && !r.contested) continue;
                var sp = cam.WorldToScreenPoint(Outpost.Content.MapBuilder.Ground(map, r.x, r.y) + Vector3.up * 4f);
                if (sp.z <= 0) continue;
                var bar = new Rect(sp.x - 30, Screen.height - sp.y, 60, 6);
                Fill(new Rect(bar.x - 1, bar.y - 1, bar.width + 2, bar.height + 2), new Color(0, 0, 0, 0.7f));
                Fill(new Rect(bar.x, bar.y, bar.width * k, bar.height),
                    r.contested ? new Color(1f, 0.85f, 0.3f) : r.progress > 0 ? new Color(0.4f, 0.85f, 1f) : new Color(1f, 0.45f, 0.9f));
            }
        }

        void DragBox()
        {
            if (!input.dragging) return;
            var r = Rect.MinMaxRect(Mathf.Min(input.dragStart.x, input.dragNow.x), Mathf.Min(input.dragStart.y, input.dragNow.y),
                Mathf.Max(input.dragStart.x, input.dragNow.x), Mathf.Max(input.dragStart.y, input.dragNow.y));
            if (r.width < 6 && r.height < 6) return;
            var line = new Color(0.35f, 1f, 0.5f, 0.9f);
            Fill(r, new Color(0.35f, 1f, 0.5f, 0.12f));
            Fill(new Rect(r.x, r.y, r.width, 1), line);
            Fill(new Rect(r.x, r.yMax, r.width, 1), line);
            Fill(new Rect(r.x, r.y, 1, r.height), line);
            Fill(new Rect(r.xMax, r.y, 1, r.height), line);
        }
    }
}
