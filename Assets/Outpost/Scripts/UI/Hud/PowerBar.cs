// PowerBar.cs · roadmap step M1.6 (filled Nov 20).
// Z X C V leader powers: four buttons at the top centre with name, cost, cooldown and a tooltip.

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using Outpost.Sim;
using Outpost.View;

namespace Outpost.UI
{
    /// <summary>
    /// The leader power bar (Python scenes/rts.py power_rects): one button per power in key order Z X C V. A click or
    /// the key starts aiming through CommandInput.TriggerPower; the next left click on the map casts it. While a power
    /// recharges a dark red bar shrinks from the right and the button shows the seconds left; when it is ready it shows
    /// the cost. The tooltip (below the bar) gives what the power does and its current cooldown.
    /// </summary>
    public class PowerBar
    {
        public readonly VisualElement root, tip;
        readonly Label tipText, leaderText;
        readonly List<(VisualElement el, VisualElement fill, Label name, Label sub)> slots =
            new List<(VisualElement, VisualElement, Label, Label)>();
        Battle battle;
        CommandInput input;
        int hovered = -1;
        readonly List<Label> keys = new List<Label>();

        public PowerBar()
        {
            root = Ui.Box("op-powers");
            root.Add(leaderText = Ui.Text("", "op-power__leader"));
            for (int i = 0; i < 4; i++)
            {
                int index = i;
                var el = Ui.Button("op-panel op-power", () => input?.TriggerPower(index));
                var fill = Ui.Box("op-power__fill");
                var key = Ui.Text(Hotkeys.Label("power_" + (i + 1)), "op-power__key");
                keys.Add(key);
                var name = Ui.Text("", "op-power__name");
                var sub = Ui.Text("", "op-power__sub");
                el.Add(fill);
                el.Add(key);
                el.Add(name);
                el.Add(sub);
                el.RegisterCallback<PointerEnterEvent>(_ => hovered = index);
                el.RegisterCallback<PointerLeaveEvent>(_ => { if (hovered == index) hovered = -1; });
                root.Add(el);
                slots.Add((el, fill, name, sub));
            }
            tip = Ui.Box("op-panel op-power-tip");
            tip.Add(tipText = Ui.Text("", "op-wrap"));
            Ui.Show(tip, false);
        }

        public void Bind(Battle b, CommandInput inp)
        {
            battle = b;
            input = inp;
            hovered = -1;
        }

        public void Refresh()
        {
            if (battle == null) return;
            leaderText.text = battle.LeaderName.ToUpper();
            var ids = battle.playerPowers;
            for (int i = 0; i < slots.Count; i++)
            {
                var s = slots[i];
                bool has = i < ids.Length;
                Ui.Show(s.el, has);
                if (!has) continue;
                string pid = ids[i];
                float cd = battle.PowerCdLeft(pid), full = Mathf.Max(1f, battle.PowerCooldown(pid));
                s.name.text = battle.PowerName(pid);
                keys[i].text = Hotkeys.Label("power_" + (i + 1));
                s.sub.text = cd > 0 ? Ui.Secs(cd) : battle.PowerCost(pid).ToString();
                s.fill.style.width = Length.Percent(Mathf.Clamp01(cd / full) * 100f);
                bool ready = battle.CanPower(pid) == null;
                s.el.EnableInClassList("op-power--ready", ready);
                s.el.EnableInClassList("op-power--off", !ready);
                s.el.EnableInClassList("op-power--lit", input != null && input.powerAim == pid);
            }
            bool showTip = hovered >= 0 && hovered < ids.Length;
            Ui.Show(tip, showTip);
            if (showTip)
            {
                string pid = ids[hovered];
                string why = battle.CanPower(pid);
                float radius = GameData.F(battle.PowerDef(pid), "radius");
                tipText.text = $"<b>{battle.PowerName(pid)}</b>   {battle.PowerCost(pid)} Supplies · cooldown {Mathf.RoundToInt(battle.PowerCooldown(pid))} s" +
                               (radius > 0 ? $" · radius {Mathf.RoundToInt(radius)}" : "") + $"   <color=#96a6bc>({Hotkeys.Label("power_" + (hovered + 1))})</color>\n" +
                               battle.PowerDesc(pid) + (why != null ? $"\n<color=#ff8270>{why}</color>" : "\nThen left click the target on the map.");
            }
        }
    }
}
