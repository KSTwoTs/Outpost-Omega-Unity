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
        readonly VisualElement mapEl, dots;
        Battle battle;
        CommandInput input;
        RtsCamera rig;
        MapData map;

        static readonly Color Mine = new Color(0.35f, 1f, 0.5f), Theirs = new Color(1f, 0.35f, 0.3f);

        public MinimapPanel()
        {
            root = Ui.Box("op-panel op-minimap", true);
            mapEl = Ui.Box("op-minimap__map", true);
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
        }

        public void Refresh() => dots.MarkDirtyRepaint();

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
            foreach (var e in battle.All())
            {
                if (!e.alive || battle.HiddenFrom(e, "player")) continue;
                float s = e.IsUnit ? 1.6f : e is Building b && b.IsHq ? 4f : 2.6f;
                Square(p, e.x * sx, e.y * sy, s, e.faction == "player" ? Mine : Theirs);
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
