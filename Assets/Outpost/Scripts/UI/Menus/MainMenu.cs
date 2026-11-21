// MainMenu.cs · roadmap step M2 (filled Nov 21, Milestone 2 group 1).
// The main menu (Python scenes/menu.py).

using UnityEngine.UIElements;

namespace Outpost.UI
{
    /// <summary>
    /// Title and the mode buttons in the Python order. Modes that are not built yet are shown dimmed with the
    /// milestone that brings them; Skirmish, Commanders, Settings and Quit work today.
    /// GameFlow wires the actions (the UI layer never reaches up into Game).
    /// </summary>
    public class MainMenu
    {
        public readonly VisualElement root;
        public System.Action onSkirmish, onCommanders, onSettings, onQuit;
        readonly Label commander;

        public MainMenu(bool settingsReady = false)
        {
            root = Ui.Box("op-screen op-menu", true);
            var head = Ui.Box("op-menu__head");
            head.Add(Ui.Text("OUTPOST OMEGA", "op-menu__title"));
            head.Add(Ui.Text("ALIEN SIEGE", "op-menu__subtitle"));
            head.Add(Ui.Text("Year 2189. Hold the Omega Core until Earth's shield is charged.", "op-menu__tag"));
            root.Add(head);
            var col = Ui.Box("op-menu__buttons");
            col.Add(Ui.TextButton("CAMPAIGN", null, "op-button op-menu-btn", "coming later"));
            col.Add(Ui.TextButton("SKIRMISH", () => onSkirmish?.Invoke(), "op-button op-menu-btn op-menu-btn--main"));
            col.Add(Ui.TextButton("SIEGE RUN", null, "op-button op-menu-btn", "coming later"));
            col.Add(Ui.TextButton("COMMAND MODES", null, "op-button op-menu-btn", "coming later"));
            col.Add(Ui.TextButton("CLASSIC DEFENSE", null, "op-button op-menu-btn", "not decided"));
            col.Add(Ui.TextButton("COMMANDERS", () => onCommanders?.Invoke(), "op-button op-menu-btn"));
            col.Add(Ui.TextButton("TECH LAB", null, "op-button op-menu-btn", "coming later"));
            col.Add(Ui.TextButton("SETTINGS", settingsReady ? (System.Action)(() => onSettings?.Invoke()) : null, "op-button op-menu-btn", settingsReady ? null : "coming soon"));
            col.Add(Ui.TextButton("QUIT", () => onQuit?.Invoke(), "op-button op-menu-btn"));
            root.Add(col);
            root.Add(commander = Ui.Text("", "op-menu__foot"));
            root.Add(Ui.Text("Unity port in progress: Skirmish is playable", "op-menu__note"));
        }

        /// <summary>Shows the active Commander along the bottom.</summary>
        public void Refresh(string commanderName) => commander.text = "Commander: " + commanderName;
    }
}
