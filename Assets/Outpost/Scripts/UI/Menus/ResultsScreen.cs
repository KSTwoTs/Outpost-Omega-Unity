// ResultsScreen.cs · roadmap step M2 (filled Nov 21, Milestone 2 group 1).
// Win or lose, the battle's numbers, the per-map record (Python scenes/rts.py RTSEndScene).

using UnityEngine.UIElements;
using Outpost.Sim;

namespace Outpost.UI
{
    /// <summary>
    /// After a battle: VICTORY / DEFEAT (or BATTLE LEFT), map, difficulty and time, then the numbers rts.py shows
    /// (aliens destroyed, losses, Supplies gathered and spent, bases standing) and a record line once saves exist
    /// (group 3 fills it through GameFlow.RecordResult). REMATCH, NEW BATTLE and MAIN MENU go back through GameFlow.
    /// </summary>
    public class ResultsScreen
    {
        public readonly VisualElement root;
        public System.Action onRematch, onSetup, onMenu;
        readonly Label title, sub, leader, record;
        readonly Label[] lines = new Label[4];
        public BattleResult Result { get; private set; }

        public ResultsScreen()
        {
            root = Ui.Box("op-screen op-results", true);
            root.Add(title = Ui.Text("", "op-results__title"));
            root.Add(sub = Ui.Text("", "op-results__sub"));
            root.Add(leader = Ui.Text("", "op-results__line"));
            for (int i = 0; i < lines.Length; i++) root.Add(lines[i] = Ui.Text("", "op-results__line"));
            root.Add(record = Ui.Text("", "op-results__record"));
            var buttons = Ui.Box("op-results__buttons");
            buttons.Add(Ui.TextButton("REMATCH", () => onRematch?.Invoke()));
            buttons.Add(Ui.TextButton("NEW BATTLE", () => onSetup?.Invoke()));
            buttons.Add(Ui.TextButton("MAIN MENU", () => onMenu?.Invoke()));
            root.Add(buttons);
        }

        /// <summary>Fills the screen. recordLine comes from GameFlow.RecordResult (null until saves exist).</summary>
        public void Show(BattleResult r, string recordLine)
        {
            Result = r;
            title.text = r.Won ? "VICTORY" : r.Left ? "BATTLE LEFT" : "DEFEAT";
            title.EnableInClassList("op-results__title--win", r.Won);
            title.EnableInClassList("op-results__title--lose", !r.Won);
            var c = r.config;
            sub.text = $"{(c != null ? c.MapName : "")}  -  {(c != null ? c.difficulty : "")}  -  {Ui.Clock(r.time)}";
            string lname = c == null ? "" : GameData.S(GameData.Commanders[c.leader], "name", c.leader);
            leader.text = c == null ? "" : "Commander: " + lname;
            lines[0].text = $"Aliens destroyed: {r.kills}";
            lines[1].text = $"Losses: {r.losses}";
            lines[2].text = $"Supplies gathered: {r.gathered}   spent: {r.spent}";
            lines[3].text = $"Bases standing: {r.bases}";
            record.text = recordLine ?? "";
            Ui.Show(record, !string.IsNullOrEmpty(recordLine));
        }
    }
}
