using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using Outpost.Content;
using Outpost.Sim;
using Outpost.View;

namespace Outpost.UI
{
    /// <summary>
    /// The battle HUD (UI Toolkit). Builds the panels once, is told which battle to show with Bind,
    /// and refreshes them a few times a second. Layouts are built in code; every color and size is
    /// in Resources/UI/Outpost.uss.
    ///
    /// The HUD only reads the battle. Everything it does goes through Battle methods or CommandInput.
    /// </summary>
    public class HudRoot : MonoBehaviour
    {
        public Battle battle;
        public CommandInput input;
        public Fx fx;
        public System.Action restart;
        public bool paused;

        UIDocument doc;
        VisualElement root, help, helpHint, overlay, overlayButton;
        Label overlayTitle, overlaySub, overlayButtonText;
        ResourceBar resources;
        AlertsPanel alerts;
        SelectionPanel selection;
        CommandCard card;
        MinimapPanel minimap;
        PowerBar powers;
        float refreshT;
        bool showHelp = true;

        const string HelpText =
            "<b>Controls</b>  <color=#96a6bc>(F1 hides)</color>\n" +
            "<b>Select:</b> left click or drag \u00b7 Shift adds \u00b7 double-click takes all of that type\n" +
            "<b>Orders:</b> right click to move or attack \u00b7 A attack-move \u00b7 S stop \u00b7 H hold \u00b7 F ability\n" +
            "<b>Powers:</b> Z X C V leader powers (or click the bar at the top), then left click the target\n" +
            "<b>Army:</b> Q whole army \u00b7 E army on screen \u00b7 G Commander \u00b7 Ctrl+1-5 set group \u00b7 1-5 recall\n" +
            "<b>Camera:</b> arrows, screen edge or middle drag \u00b7 wheel zooms \u00b7 Page Up/Down rotates \u00b7 Home resets\n" +
            "<b>Minimap:</b> left click moves the camera \u00b7 right click sends the selected units\n" +
            "<b>Building:</b> select the Command Post \u00b7 hover a button for details \u00b7 right click sets a rally point\n" +
            "P pauses.  <color=#ffd77a>Goal: destroy the alien Hive.</color>";

        void Awake()
        {
            var go = new GameObject("HUD");
            go.transform.SetParent(transform, false);
            go.SetActive(false);
            doc = go.AddComponent<UIDocument>();
            var settings = ScriptableObject.CreateInstance<PanelSettings>();
            settings.name = "Outpost HUD";
            settings.themeStyleSheet = Resources.Load<ThemeStyleSheet>("UI/OutpostTheme");
            settings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            settings.referenceResolution = new Vector2Int(1920, 1080);
            settings.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            settings.match = 1f;                      // sizes follow the screen's height
            doc.panelSettings = settings;
            go.SetActive(true);
            if (settings.themeStyleSheet == null) Debug.LogError("[Outpost] Resources/UI/OutpostTheme.tss is missing: the HUD has no theme.");

            root = doc.rootVisualElement;
            root.pickingMode = PickingMode.Ignore;
            var sheet = Resources.Load<StyleSheet>("UI/Outpost");
            if (sheet != null) root.styleSheets.Add(sheet);
            else Debug.LogError("[Outpost] Resources/UI/Outpost.uss is missing: the HUD has no styles.");
            root.AddToClassList("op-root");

            resources = new ResourceBar();
            alerts = new AlertsPanel();
            selection = new SelectionPanel();
            card = new CommandCard();
            minimap = new MinimapPanel();
            powers = new PowerBar();

            var bottom = Ui.Box("op-bottom");
            var cardWrap = Ui.Box("");
            cardWrap.Add(card.root);
            cardWrap.Add(card.tip);
            bottom.Add(selection.root);
            bottom.Add(cardWrap);

            help = Ui.Box("op-panel op-help", true);
            help.Add(Ui.Text(HelpText, "op-wrap"));
            helpHint = Ui.Text("F1: controls", "op-help-hint");

            overlay = Ui.Box("op-overlay", true);
            overlay.Add(overlayTitle = Ui.Text("", "op-overlay__title"));
            overlay.Add(overlaySub = Ui.Text("", "op-overlay__sub"));
            overlayButton = Ui.Button("op-button", OverlayClick);
            overlayButton.Add(overlayButtonText = Ui.Text(""));
            overlay.Add(overlayButton);

            root.Add(alerts.root);
            root.Add(alerts.toast);
            root.Add(bottom);
            root.Add(minimap.root);
            root.Add(resources.root);
            root.Add(powers.root);
            root.Add(powers.tip);
            root.Add(help);
            root.Add(helpHint);
            root.Add(overlay);
            Ui.Show(overlay, false);
        }

        /// <summary>Points the HUD at a (new) battle.</summary>
        public void Bind(Battle b, CommandInput inp, Fx effects, RtsCamera rig, MapData map, Texture2D minimapPicture, System.Action onRestart)
        {
            battle = b;
            input = inp;
            fx = effects;
            restart = onRestart;
            paused = false;
            selection.Bind(b, inp);
            card.Bind(b, inp);
            minimap.Bind(b, inp, rig, map, minimapPicture);
            powers.Bind(b, inp);
            input.overUi = OverUi;
            refreshT = 0;
        }

        /// <summary>Is this screen position (pixels, bottom-left origin) on top of a HUD panel?</summary>
        public bool OverUi(Vector2 screen)
        {
            var panel = root?.panel;
            if (panel == null) return false;
            var p = RuntimePanelUtils.ScreenToPanel(panel, new Vector2(screen.x, Screen.height - screen.y));
            var hit = panel.Pick(p);
            return hit != null && hit != root;
        }

        void OverlayClick()
        {
            if (battle == null) return;
            if (battle.winner != null) restart?.Invoke();
            else paused = false;
        }

        void Update()
        {
            PortraitLibrary.Tick();
            if (battle == null || input == null) return;
            var kb = Keyboard.current;
            if (kb != null)
            {
                if (kb.pKey.wasPressedThisFrame && battle.winner == null) paused = !paused;
                if (kb.f1Key.wasPressedThisFrame) showHelp = !showHelp;
            }
            alerts.Refresh(fx, input);
            refreshT -= Time.unscaledDeltaTime;
            if (refreshT > 0) return;
            refreshT = 0.1f;

            resources.Refresh(battle);
            selection.Refresh();
            card.Refresh();
            minimap.Refresh();
            powers.Refresh();
            Ui.Show(help, showHelp);
            Ui.Show(helpHint, !showHelp);

            bool over = battle.winner != null || paused;
            Ui.Show(overlay, over);
            if (!over) return;
            if (battle.winner == "player")
            {
                overlayTitle.text = "<color=#78eb96>VICTORY</color>";
                overlaySub.text = $"Time {Ui.Clock(battle.time)}";
                overlayButtonText.text = "Play again";
            }
            else if (battle.winner != null)
            {
                overlayTitle.text = "<color=#ff8270>DEFEAT</color>";
                overlaySub.text = $"Time {Ui.Clock(battle.time)}";
                overlayButtonText.text = "Play again";
            }
            else
            {
                overlayTitle.text = "PAUSED";
                overlaySub.text = "";
                overlayButtonText.text = "Resume";
            }
        }
    }
}
