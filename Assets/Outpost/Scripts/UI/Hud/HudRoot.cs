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
        /// <summary>Pause screen: back to the menu (BattleSession sets it when the game flow is running; null = no button).</summary>
        public System.Action quit;
        /// <summary>The end screen's button: Play again (Battle.unity played directly) or Continue (to the results screen).</summary>
        public string endButtonText = "Play again";
        /// <summary>Pause menu RESTART: same setup, new seed (BattleSession sets it; null = no button).</summary>
        public System.Action restartFromPause;
        /// <summary>The pause menu, and the settings screen opened from it (BattleSession hands it over with SetSettings).</summary>
        [System.NonSerialized] public PauseMenu pauseMenu;
        [System.NonSerialized] public SettingsScreen settings;
        bool settingsOpen;
        public bool SettingsOpen => settingsOpen;

        UIDocument doc;
        VisualElement root, helpHint, overlay, overlayButton;
        /// <summary>The controls overlay (F1) and the first-match hints (BattleSession starts them).</summary>
        [System.NonSerialized] public HelpOverlay helpOverlay;
        [System.NonSerialized] public FirstMatchHints hints;
        Label overlayTitle, overlaySub, overlayButtonText;
        ResourceBar resources;
        AlertsPanel alerts;
        SelectionPanel selection;
        CommandCard card;
        MinimapPanel minimap;
        PowerBar powers;
        float refreshT;
        bool showHelp;
        int helpVersion = -1;

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
            Ui.Track(settings);
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

            helpOverlay = new HelpOverlay();
            helpOverlay.onClose = () => showHelp = false;
            hints = new FirstMatchHints();
            helpHint = Ui.Text(Hotkeys.Label("help") + ": controls", "op-help-hint");

            overlay = Ui.Box("op-overlay", true);
            overlay.Add(overlayTitle = Ui.Text("", "op-overlay__title"));
            overlay.Add(overlaySub = Ui.Text("", "op-overlay__sub"));
            overlayButton = Ui.Button("op-button", OverlayClick);
            overlayButton.Add(overlayButtonText = Ui.Text(""));
            overlay.Add(overlayButton);
            pauseMenu = new PauseMenu();
            pauseMenu.onResume = () => paused = false;

            root.Add(alerts.root);
            root.Add(alerts.toast);
            root.Add(bottom);
            root.Add(minimap.root);
            root.Add(resources.root);
            root.Add(powers.root);
            root.Add(powers.tip);
            root.Add(hints.root);
            root.Add(helpOverlay.root);
            root.Add(helpHint);
            root.Add(overlay);
            Ui.Show(overlay, false);
            root.Add(pauseMenu.root);
            Ui.Show(pauseMenu.root, false);
        }

        /// <summary>Points the HUD at a (new) battle.</summary>
        public void Bind(Battle b, CommandInput inp, Fx effects, RtsCamera rig, MapData map, Texture2D minimapPicture, System.Action onRestart)
        {
            battle = b;
            input = inp;
            fx = effects;
            restart = onRestart;
            paused = false;
            CloseSettings();
            hints.Stop();
            showHelp = false;
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

        /// <summary>Puts the settings screen into the HUD (shown from the pause menu).</summary>
        public void SetSettings(SettingsScreen s)
        {
            if (settings != null) settings.root.RemoveFromHierarchy();
            settings = s;
            if (s == null) return;
            s.onBack = CloseSettings;
            s.root.AddToClassList("op-settings--overlay");
            root.Add(s.root);
            Ui.Show(s.root, false);
        }

        /// <summary>Pause menu SETTINGS: the battle stays paused while it is open.</summary>
        public void OpenSettings()
        {
            if (settings == null) return;
            settingsOpen = true;
            paused = true;
            settings.Open();
            Ui.Show(settings.root, true);
            Ui.Show(pauseMenu.root, false);
        }

        public void CloseSettings()
        {
            settingsOpen = false;
            if (settings != null) Ui.Show(settings.root, false);
            refreshT = 0;
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
            // Esc / P (Python): resume if paused, else drop whatever is being aimed, else pause
            if (settingsOpen)
            {
                settings.Tick();
                if (Hotkeys.PressedAny("cancel") || Hotkeys.PressedAny("menu_back")) settings.Back();
            }
            else if (battle.winner == null && (Hotkeys.PressedAny("pause") || Hotkeys.PressedAny("cancel")))
            {
                if (paused) paused = false;
                else if (!input.CancelMode()) paused = true;
            }
            if (Hotkeys.PressedAny("help")) showHelp = !showHelp;
            input.frozen = paused;
            Markers.Suppressed = paused || settingsOpen || showHelp;
            if (!paused && battle.winner == null)
                for (int i = 0; i < Hotkeys.CardGrid.Length; i++)
                    if (Hotkeys.Pressed("card_" + (i + 1))) card.Press(i);
            if (helpVersion != Hotkeys.Version)
            {
                helpVersion = Hotkeys.Version;
                ((Label)helpHint).text = Hotkeys.Label("help") + ": controls";
            }
            alerts.Refresh(fx, input);
            refreshT -= Time.unscaledDeltaTime;
            if (refreshT > 0) return;
            refreshT = 0.1f;

            resources.Refresh(battle, input.speed);
            selection.Refresh();
            card.Refresh();
            minimap.Refresh();
            powers.Refresh();
            bool ended = battle.winner != null;
            if (showHelp != helpOverlay.Visible) helpOverlay.Show(showHelp);
            if (showHelp) helpOverlay.Refresh();
            Ui.Show(helpHint, !showHelp);
            hints.Tick(0.1f, !paused && !ended && !settingsOpen && !showHelp);
            Ui.Show(overlay, ended);
            bool showPause = paused && !ended && !settingsOpen;
            Ui.Show(pauseMenu.root, showPause);
            if (showPause)
            {
                pauseMenu.onSettings = settings != null ? (System.Action)OpenSettings : null;
                pauseMenu.onRestart = restartFromPause != null ? (System.Action)(() => restartFromPause()) : null;
                pauseMenu.onQuit = quit != null ? (System.Action)(() => quit()) : null;
                pauseMenu.Refresh("Time " + Ui.Clock(battle.time));
            }
            if (!ended) return;
            if (battle.winner == "player")
            {
                overlayTitle.text = "<color=#78eb96>VICTORY</color>";
                overlaySub.text = $"Time {Ui.Clock(battle.time)}";
                overlayButtonText.text = endButtonText;
            }
            else if (battle.winner != null)
            {
                overlayTitle.text = "<color=#ff8270>DEFEAT</color>";
                overlaySub.text = $"Time {Ui.Clock(battle.time)}";
                overlayButtonText.text = endButtonText;
            }
        }
    }
}
