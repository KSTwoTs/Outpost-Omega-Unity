using UnityEngine;
using UnityEngine.UIElements;
using Outpost.Content;
using Outpost.Sim;
using Outpost.View;

namespace Outpost.UI
{
    /// <summary>
    /// Bottom-right minimap: the map picture, a dot per unit and building, the camera frame.
    /// Left click or drag moves the camera; right click sends the selected units there.
    /// </summary>
    public class MinimapPanel
    {
        public readonly VisualElement root;
        readonly VisualElement mapEl, fogEl, dots;
        Texture2D fogTex;
        Color32[] fogPx;
        int fogVersion = -1;
        const byte MiniUnexplored = 255, MiniExplored = 120;     // render.py mini_alpha_table
        Battle battle;
        CommandInput input;
        RtsCamera rig;
        MapData map;

        static readonly Color Mine = new Color(0.35f, 1f, 0.5f), Theirs = new Color(1f, 0.35f, 0.3f), Wild = new Color(0.8f, 0.6f, 1f);

        public MinimapPanel()
        {
            root = Ui.Box("op-panel op-minimap", true);
            mapEl = Ui.Box("op-minimap__map", true);
            fogEl = Ui.Box("op-minimap__dots");
            fogEl.pickingMode = PickingMode.Ignore;
            mapEl.Add(fogEl);
            dots = Ui.Box("op-minimap__dots");
            dots.generateVisualContent += Paint;
            mapEl.Add(dots);
            root.Add(mapEl);
            mapEl.RegisterCallback<PointerDownEvent>(e => Click(e.localPosition, e.button));
            mapEl.RegisterCallback<PointerMoveEvent>(e =>
            {
                if ((e.pressedButtons & 1) != 0) Click(e.localPosition, 0);
            });
        }

        public void Bind(Battle b, CommandInput inp, RtsCamera camRig, MapData m, Texture2D picture)
        {
            battle = b;
            input = inp;
            rig = camRig;
            map = m;
            float w = 300f, h = w * m.px_h / m.px_w;
            mapEl.style.width = w;
            mapEl.style.height = h;
            mapEl.style.backgroundImage = picture != null ? new StyleBackground(picture) : StyleKeyword.None;
            var g = b.FogOf("player");
            if (fogTex != null) UnityEngine.Object.Destroy(fogTex);
            fogTex = null;
            fogVersion = -1;
            if (g != null)
            {
                fogTex = new Texture2D(g.W, g.H, TextureFormat.RGBA32, false)
                {
                    name = "fog_minimap", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear,
                };
                fogPx = new Color32[g.W * g.H];
            }
            fogEl.style.backgroundImage = fogTex != null ? new StyleBackground(fogTex) : StyleKeyword.None;
            RefreshFog();
        }

        public void Refresh()
        {
            RefreshFog();
            dots.MarkDirtyRepaint();
        }

        /// <summary>render.py draw_minimap: the fog over the map picture (alpha 255 unexplored, 120 explored, clear in sight).</summary>
        void RefreshFog()
        {
            var g = battle?.FogOf("player");
            if (g == null || fogTex == null || g.version == fogVersion) return;
            fogVersion = g.version;
            fogEl.style.display = g.revealAll ? DisplayStyle.None : DisplayStyle.Flex;
            if (g.revealAll) return;
            FogView.Paint(g, fogPx, MiniUnexplored, MiniExplored);
            fogTex.SetPixels32(fogPx);
            fogTex.Apply(false);
        }

        void Click(Vector2 local, int button)
        {
            if (battle == null || map == null) return;
            var r = mapEl.contentRect;
            if (r.width <= 0) return;
            var px = new Vector2(local.x / r.width * map.px_w, local.y / r.height * map.px_h);
            if (button == 0 && rig != null) rig.JumpTo(MapBuilder.Ground(map, px.x, px.y));
            else if (button == 1) battle.OrderMove(input.MyUnits, px, false);
        }

        void Paint(MeshGenerationContext ctx)
        {
            if (battle == null || map == null) return;
            var r = dots.contentRect;
            float sx = r.width / map.px_w, sy = r.height / map.px_h;
            var p = ctx.painter2D;
            foreach (var s in battle.FreeSites())
                if (battle.Explored("player", s.x, s.y)) Square(p, s.x * sx, s.y * sy, 2.4f, new Color(1f, 0.85f, 0.25f, 0.9f));
            foreach (var rl in battle.relays)
                if (battle.Explored("player", rl.x, rl.y)) Square(p, rl.x * sx, rl.y * sy, 3.2f, rl.owner == "player" ? new Color(0.4f, 0.85f, 1f) : rl.owner == "alien" ? new Color(1f, 0.45f, 0.9f) : Color.white);
            foreach (var e in battle.All())
            {
                if (!e.alive || !battle.Seen(e, "player")) continue;
                float s = e.IsUnit ? 1.6f : e is Building b && b.IsHq ? 4f : 2.6f;
                Square(p, e.x * sx, e.y * sy, s, e.faction == "player" ? Mine : e.faction == "wild" ? Wild : Theirs);
            }
            // alert pings: the last six alerts pulse red for 3 s (render.py draw_minimap)
            for (int i = Mathf.Max(0, battle.alerts.Count - 6); i < battle.alerts.Count; i++)
            {
                var a = battle.alerts[i];
                float age = battle.time - a.t;
                if (age < 0 || age >= 3f) continue;
                p.strokeColor = new Color(1f, 0.3f, 0.25f, 1f - age / 6f);
                p.lineWidth = 1.5f;
                p.BeginPath();
                p.Arc(new Vector2(a.pos.x * sx, a.pos.y * sy), 4f + (age % 1f) * 14f, Angle.Degrees(0f), Angle.Degrees(360f));
                p.Stroke();
            }
            if (rig != null)
            {
                var c = rig.transform.position;
                float cx = c.x / MapBuilder.Unit * sx, cy = -c.z / MapBuilder.Unit * sy;
                p.strokeColor = Color.white;
                p.lineWidth = 1.5f;
                p.BeginPath();
                p.MoveTo(new Vector2(cx - 24, cy - 15));
                p.LineTo(new Vector2(cx + 24, cy - 15));
                p.LineTo(new Vector2(cx + 24, cy + 15));
                p.LineTo(new Vector2(cx - 24, cy + 15));
                p.ClosePath();
                p.Stroke();
            }
        }

        static void Square(Painter2D p, float x, float y, float s, Color c)
        {
            p.fillColor = c;
            p.BeginPath();
            p.MoveTo(new Vector2(x - s, y - s));
            p.LineTo(new Vector2(x + s, y - s));
            p.LineTo(new Vector2(x + s, y + s));
            p.LineTo(new Vector2(x - s, y + s));
            p.ClosePath();
            p.Fill();
        }
    }
}
