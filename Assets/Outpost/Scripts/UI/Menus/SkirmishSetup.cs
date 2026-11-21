// SkirmishSetup.cs · roadmap step M2 (filled Nov 21, Milestone 2 group 1).
// Map, size, difficulty, leader and tower loadout before a Skirmish (Python scenes/rts.py RTSSetupScene).

using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UIElements;
using Outpost.Content;
using Outpost.Sim;

namespace Outpost.UI
{
    /// <summary>
    /// The Skirmish setup: battlefield (the exported maps, with a preview of bases and relays), map size (only Medium
    /// exists until the map generator, M3), difficulty Easy..Legendary, the Commander (name, role, passive and V power
    /// from the data; CHANGE / DETAILS opens the Commanders screen) and the tower loadout, up to
    /// TowerBehaviors.PerCategory per category with the oldest pick dropped when a category is full (rts.py rule).
    /// Works on a copy of the config; DEPLOY hands it to onDeploy.
    /// </summary>
    public class SkirmishSetup
    {
        public readonly VisualElement root;
        public System.Action<BattleConfig> onDeploy;
        public System.Action onBack, onCommanders;
        /// <summary>Group 3 (saves): the record line for a map + difficulty, e.g. Record here: 3W 1L best 12:04 (null = none).</summary>
        public System.Func<BattleConfig, string> recordLine;

        BattleConfig cfg = BattleConfig.Default();
        readonly Dictionary<string, VisualElement> mapButtons = new Dictionary<string, VisualElement>();
        readonly Dictionary<string, VisualElement> diffButtons = new Dictionary<string, VisualElement>();
        readonly Dictionary<string, VisualElement> towerButtons = new Dictionary<string, VisualElement>();
        readonly Dictionary<string, VisualElement> towerIcons = new Dictionary<string, VisualElement>();
        readonly HashSet<string> pictured = new HashSet<string>();
        readonly Dictionary<string, Texture2D> previews = new Dictionary<string, Texture2D>();
        readonly Dictionary<string, string> previewInfo = new Dictionary<string, string>();
        readonly Dictionary<string, List<Vector3>> previewDots = new Dictionary<string, List<Vector3>>();   // x, y (0..1), type
        readonly Label[] catCounts = new Label[3];
        readonly VisualElement preview, tipBox, leaderPortrait, leaderLines;
        readonly Label info, leaderName, leaderRole, leaderDesc, vPower, loadoutHead, tipText, record;
        bool leaderPictured;

        public BattleConfig Config => cfg;

        public SkirmishSetup()
        {
            root = Ui.Box("op-screen op-col", true);
            root.Add(Ui.Text("SKIRMISH", "op-h1"));
            var cols = Ui.Box("op-setup__cols");
            root.Add(cols);

            // ---- battlefield
            var left = Ui.Box("op-setup__left");
            cols.Add(left);
            left.Add(Ui.Heading("Battlefield"));
            foreach (var id in MapBuilder.Available())
            {
                string mid = id;
                var b = Ui.TextButton(BattleConfig.MapNameOf(id), () => { cfg.mapId = mid; Refresh(); }, "op-button op-list-btn");
                mapButtons[id] = b;
                left.Add(b);
            }
            left.Add(Ui.Para("More maps and random maps come with the map generator."));
            preview = Ui.Box("op-preview");
            left.Add(preview);
            left.Add(info = Ui.Text("", "op-p"));
            left.Add(Ui.Para("cyan = you   magenta = Hive   yellow = expansion   square = relay"));

            // ---- difficulty, size, Commander
            var mid2 = Ui.Box("op-setup__mid");
            cols.Add(mid2);
            mid2.Add(Ui.Heading("Difficulty"));
            var diffs = Ui.Box("op-wraprow");
            foreach (var d in BattleConfig.Difficulties)
            {
                string dd = d;
                var b = Ui.TextButton(d, () => { cfg.difficulty = dd; Refresh(); }, "op-button op-chip");
                diffButtons[d] = b;
                diffs.Add(b);
            }
            mid2.Add(diffs);
            mid2.Add(Ui.Heading("Map size"));
            var sizes = Ui.Box("op-wraprow");
            foreach (var s in BattleConfig.Sizes)
            {
                string label = char.ToUpper(s[0]) + s.Substring(1);
                var b = Ui.TextButton(label, BattleConfig.SizeAvailable(s) ? (System.Action)(() => { }) : null, "op-button op-chip");
                Ui.SetOn(b, s == "medium");
                sizes.Add(b);
            }
            mid2.Add(sizes);
            mid2.Add(Ui.Para("Small and Large maps come with the map generator."));
            mid2.Add(Ui.Heading("Commander"));
            var card = Ui.Button("op-button op-leader", () => onCommanders?.Invoke());
            leaderPortrait = Ui.Box("op-portrait op-leader__portrait");
            card.Add(leaderPortrait);
            var who = Ui.Box("op-col op-grow");
            who.Add(leaderName = Ui.Text("", "op-leader__name"));
            who.Add(leaderRole = Ui.Text("", "op-dim"));
            card.Add(who);
            card.Add(Ui.Text("CHANGE / DETAILS >", "op-leader__change"));
            mid2.Add(card);
            mid2.Add(leaderDesc = Ui.Text("", "op-p op-p--bright"));
            leaderDesc.style.marginTop = 8;
            leaderLines = Ui.Box("op-col");
            mid2.Add(leaderLines);
            mid2.Add(vPower = Ui.Text("", "op-p"));
            vPower.style.marginTop = 6;
            mid2.Add(record = Ui.Text("", "op-h3"));

            // ---- tower loadout
            var right = Ui.Box("op-setup__right");
            cols.Add(right);
            right.Add(loadoutHead = Ui.Heading(""));
            right.Add(Ui.Para("These are the towers your Command Posts can build on their turret sockets (the Bunker is always available). Hover a tower for details."));
            var cats = Ui.Box("op-row");
            right.Add(cats);
            for (int c = 0; c < TowerBehaviors.Categories.Length; c++)
            {
                var col = Ui.Box("op-loadout__col");
                col.Add(catCounts[c] = Ui.Text("", "op-h3"));
                foreach (var kind in TowerBehaviors.TowersIn(TowerBehaviors.Categories[c]))
                {
                    string k = kind;
                    var d = GameData.Tower(k);
                    var b = Ui.Button("op-button op-tower", () => { TowerBehaviors.ToggleLoadout(cfg.loadout, k); Refresh(); });
                    var icon = Ui.Box("op-portrait op-tower__icon");
                    b.Add(icon);
                    b.Add(Ui.Text(GameData.S(d, "name", k)));
                    b.RegisterCallback<PointerEnterEvent>(_ => ShowTip(k));
                    b.RegisterCallback<PointerLeaveEvent>(_ => Ui.Show(tipBox, false));
                    towerButtons[k] = b;
                    towerIcons[k] = icon;
                    col.Add(b);
                }
                cats.Add(col);
            }

            tipBox = Ui.Box("op-panel op-setup__tip");
            tipBox.Add(tipText = Ui.Text("", "op-wrap"));
            Ui.Show(tipBox, false);
            root.Add(tipBox);

            var foot = Ui.Box("op-footer");
            foot.Add(Ui.TextButton("BACK", () => onBack?.Invoke(), "op-button op-big"));
            foot.Add(Ui.TextButton("DEPLOY", Deploy, "op-button op-big op-menu-btn--main"));
            root.Add(foot);
        }

        /// <summary>Shows the screen for a copy of this config (the last setup).</summary>
        public void Open(BattleConfig c)
        {
            cfg = (c ?? BattleConfig.Default()).Clone().Validated();
            Refresh();
        }

        /// <summary>The Commanders screen picked someone: they lead this Skirmish too.</summary>
        public void SetLeader(string id)
        {
            cfg.leader = id;
            cfg.Validated();
            leaderPictured = false;
            Refresh();
        }

        public void PickMap(string id) { cfg.mapId = id; Refresh(); }
        public void PickDifficulty(string d) { cfg.difficulty = d; Refresh(); }
        public void ToggleTower(string kind) { TowerBehaviors.ToggleLoadout(cfg.loadout, kind); Refresh(); }

        public void Deploy() => onDeploy?.Invoke(cfg.Clone());

        /// <summary>Keeps asking PortraitLibrary for the tower and Commander pictures until they are ready.</summary>
        public void Tick()
        {
            if (root.panel == null) return;
            int asked = 0;
            foreach (var kv in towerIcons)
            {
                if (pictured.Contains(kv.Key)) continue;
                if (Ui.Portrait(kv.Value, "tur_" + kv.Key, null)) pictured.Add(kv.Key);
                if (++asked >= 4) break;      // a few per frame
            }
            if (!leaderPictured) leaderPictured = Ui.Portrait(leaderPortrait, "commander", leaderName.text);
        }

        void ShowTip(string kind)
        {
            var d = GameData.Tower(kind);
            int cost = (int)(GameData.F(d, "cost", 100) * GameData.F(GameData.Rts["towers"], "cost_mult", 2f));
            tipText.text = $"<b>{GameData.S(d, "name", kind)}</b>  -  {cost} Supplies   <color=#96a6bc>{GameData.S(d, "category", "")}</color>\n{GameData.S(d, "description", "")}";
            Ui.Show(tipBox, true);
        }

        void Refresh()
        {
            foreach (var kv in mapButtons) Ui.SetOn(kv.Value, kv.Key == cfg.mapId);
            foreach (var kv in diffButtons) Ui.SetOn(kv.Value, kv.Key == cfg.difficulty);
            foreach (var kv in towerButtons) Ui.SetOn(kv.Value, cfg.loadout.Contains(kv.Key));
            int per = TowerBehaviors.PerCategory;
            loadoutHead.text = $"Tower loadout  ({per} per category, {cfg.loadout.Count} picked)";
            for (int c = 0; c < catCounts.Length; c++)
                catCounts[c].text = $"{TowerBehaviors.CategoryLabels[c]} {TowerBehaviors.CountIn(cfg.loadout, TowerBehaviors.Categories[c])}/{per}";

            var hero = GameData.Commanders[cfg.leader];
            leaderName.text = GameData.S(hero, "name", cfg.leader).ToUpper();
            leaderRole.text = GameData.S(hero?["info"], "role", "");
            leaderDesc.text = GameData.S(GameData.Rts["leaders"]?[cfg.leader], "desc", "");
            leaderLines.Clear();
            leaderLines.Add(Ui.Bullets(CommandersScreen.PassiveLines(cfg.leader)));
            var lp = GameData.Rts["leader_powers"]?[cfg.leader];
            vPower.text = lp == null ? "" : $"V power: {GameData.S(lp, "name", "")} - {GameData.S(lp, "desc", "")}";
            string rec = recordLine?.Invoke(cfg);
            record.text = rec ?? "";
            Ui.Show(record, !string.IsNullOrEmpty(rec));
            ShowPreview(cfg.mapId);
        }

        void ShowPreview(string id)
        {
            if (!previews.ContainsKey(id)) LoadPreview(id);
            var tex = previews[id];
            preview.style.backgroundImage = tex != null ? new StyleBackground(tex) : new StyleBackground(StyleKeyword.None);
            preview.Clear();
            foreach (var d in previewDots[id])
            {
                string cls = d.z == 1 ? "op-preview__dot op-preview__dot--player" : d.z == 2 ? "op-preview__dot op-preview__dot--alien"
                    : d.z == 3 ? "op-preview__dot op-preview__dot--relay" : "op-preview__dot";
                var dot = Ui.Box(cls);
                dot.style.left = Length.Percent(d.x * 100);
                dot.style.top = Length.Percent(d.y * 100);
                preview.Add(dot);
            }
            info.text = previewInfo[id];
        }

        /// <summary>The map's painted ground and its sites and relays, read from the exported files (no terrain is built).</summary>
        void LoadPreview(string id)
        {
            Texture2D tex = null;
            var dots = new List<Vector3>();
            string line = "";
            try
            {
                var png = Path.Combine(MapBuilder.Root, id + "_albedo.png");
                if (File.Exists(png))
                {
                    tex = new Texture2D(2, 2) { name = "preview_" + id };
                    tex.LoadImage(File.ReadAllBytes(png));
                }
                var j = JObject.Parse(File.ReadAllText(Path.Combine(MapBuilder.Root, id + ".json")));
                float w = GameData.F(j, "px_w", 1), h = GameData.F(j, "px_h", 1);
                int sites = 0, relays = 0;
                if (j["sites"] is JArray sa)
                    foreach (var s in sa)
                    {
                        sites++;
                        string main = GameData.S(s, "main");
                        dots.Add(new Vector3(GameData.F(s, "x") / w, GameData.F(s, "y") / h, main == "player" ? 1 : main == "alien" ? 2 : 0));
                    }
                if (j["relays"] is JArray ra)
                    foreach (var r in ra)
                    {
                        relays++;
                        dots.Add(new Vector3((float)r[0] / w, (float)r[1] / h, 3));
                    }
                string theme = GameData.S(j, "theme", "");
                if (theme.Length > 0) theme = char.ToUpper(theme[0]) + theme.Substring(1);
                line = $"{theme}  |  {GameData.I(j, "w")}x{GameData.I(j, "h")}  |  {Mathf.Max(0, sites - 2)} expansions  |  {relays} relays";
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[Outpost] Map preview for " + id + ": " + e.Message);
            }
            previews[id] = tex;
            previewDots[id] = dots;
            previewInfo[id] = line;
        }
    }
}
