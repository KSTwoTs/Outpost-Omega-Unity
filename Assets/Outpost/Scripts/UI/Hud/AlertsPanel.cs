using UnityEngine;
using UnityEngine.UIElements;
using Outpost.View;

namespace Outpost.UI
{
    /// <summary>Top-centre messages (alerts from the battle), the current aim mode, and the short "can't do that" toast.</summary>
    public class AlertsPanel
    {
        public readonly VisualElement root, toast;
        readonly Label banner, toastText;
        readonly Label[] lines = new Label[3];

        public AlertsPanel()
        {
            root = Ui.Box("op-alerts");
            root.Add(banner = Ui.Text("", "op-banner"));
            for (int i = 0; i < lines.Length; i++) root.Add(lines[i] = Ui.Text("", "op-alert"));
            toast = toastText = Ui.Text("", "op-toast");
        }

        public void Refresh(Fx fx, CommandInput input)
        {
            string mode = input.powerAim != null && input.battle != null ? input.battle.PowerName(input.powerAim).ToUpper() + ": left click the target (Esc cancels)"
                : input.scanFrom != null ? "SCAN: left click anywhere on the map (Esc cancels)"
                : input.abilityMode ? "ABILITY: left click where to use it (Esc cancels)"
                : input.attackMode ? "ATTACK-MOVE: left click a target or the ground" : null;
            Ui.Show(banner, mode != null);
            if (mode != null) banner.text = mode;

            int shown = 0;
            for (int i = fx.messages.Count - 1; i >= 0 && shown < lines.Length; i--)
            {
                var m = fx.messages[i];
                if (Time.time - m.t > 6f) break;
                var l = lines[shown++];
                l.text = m.text;
                l.EnableInClassList("op-alert--alien", m.faction == "alien");
                l.EnableInClassList("op-alert--player", m.faction != "alien");
                Ui.Show(l, true);
            }
            for (int i = shown; i < lines.Length; i++) Ui.Show(lines[i], false);

            bool t = input.toastT > 0 && !string.IsNullOrEmpty(input.toast);
            Ui.Show(toastText, t);
            if (t) toastText.text = input.toast;
        }
    }
}
