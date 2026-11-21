// PauseMenu.cs · roadmap step M2 (filled Nov 21, Milestone 2 group 3).
// The pause menu (takes over the pause overlay in HudRoot).

using UnityEngine.UIElements;

namespace Outpost.UI
{
    /// <summary>
    /// PAUSED with RESUME, SETTINGS, RESTART and QUIT TO MENU. HudRoot shows it while the battle is paused and wires
    /// the buttons; BattleSession supplies restart (same setup, new seed) and quit (back to the menu through GameFlow).
    /// Buttons without an action are hidden (Battle.unity played directly has no menu to quit to). Neither restart
    /// nor quit counts toward the map record (Python only records battles that have a winner).
    /// </summary>
    public class PauseMenu
    {
        public readonly VisualElement root;
        public System.Action onResume, onSettings, onRestart, onQuit;
        readonly VisualElement settingsButton, restartButton, quitButton;
        readonly Label sub;

        public PauseMenu()
        {
            root = Ui.Box("op-overlay op-pause", true);
            root.Add(Ui.Text("PAUSED", "op-overlay__title"));
            root.Add(sub = Ui.Text("", "op-overlay__sub"));
            var col = Ui.Box("op-pause__buttons");
            col.Add(Ui.TextButton("RESUME", () => onResume?.Invoke()));
            col.Add(settingsButton = Ui.TextButton("SETTINGS", () => onSettings?.Invoke()));
            col.Add(restartButton = Ui.TextButton("RESTART", () => onRestart?.Invoke()));
            col.Add(quitButton = Ui.TextButton("QUIT TO MENU", () => onQuit?.Invoke(), "op-button op-button--second"));
            root.Add(col);
            root.Add(Ui.Text("Restarting or quitting doesn't count toward the map record.", "op-dim"));
        }

        /// <summary>Hides the buttons that have no action and shows the battle time.</summary>
        public void Refresh(string line)
        {
            sub.text = line ?? "";
            Ui.Show(settingsButton, onSettings != null);
            Ui.Show(restartButton, onRestart != null);
            Ui.Show(quitButton, onQuit != null);
        }
    }
}
