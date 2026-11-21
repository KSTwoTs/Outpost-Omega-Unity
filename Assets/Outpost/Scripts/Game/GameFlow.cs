// GameFlow.cs · roadmap step M2 (filled Nov 21, Milestone 2 group 1).
// Moves between Boot, the menu and a battle; knows which mode is running.

using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using Outpost.Content;
using Outpost.Sim;
using Outpost.UI;

namespace Outpost.Game
{
    /// <summary>
    /// The game outside a battle. One GameFlow lives for the whole run of the game (DontDestroyOnLoad): it is in
    /// Boot.unity, and it makes itself when MainMenu.unity is played directly in the editor. It owns the menu
    /// document and its screens (main menu, Skirmish setup, Commanders, results), remembers the last setup, the
    /// shared Commander pick and the last result, and moves between the scenes:
    /// Boot -> MainMenu (main menu / setup / Commanders) -> Battle (BattleSession) -> MainMenu (results) -> ...
    /// Since group 3 it loads and writes save.json (SaveData) and settings.json (Settings) and has the Settings screen.
    /// </summary>
    public class GameFlow : MonoBehaviour
    {
        public const string BootScene = "Boot", MenuScene = "MainMenu", BattleScene = "Battle";
        public enum Screen { Main, Setup, Commanders, Results, Settings }

        public static GameFlow Instance { get; private set; }

        /// <summary>menu, or the mode of the battle being played (skirmish; campaign / siege run from M3).</summary>
        public string Mode { get; private set; } = "menu";
        /// <summary>The last Skirmish setup (the setup screen opens on it).</summary>
        public BattleConfig setup;
        /// <summary>The Commander used in every mode (Commanders screen; saved from group 3 on).</summary>
        public string commander = "vanguard";
        public BattleResult lastResult;
        /// <summary>The save file (records, Commander, last setup, unlocks). Loaded in Awake, written by SaveProfile.</summary>
        [System.NonSerialized] public SaveData save;
        /// <summary>How many battles have been started since the game launched (tests).</summary>
        public int battlesStarted;

        VisualElement root;
        MainMenu mainMenu;
        SkirmishSetup setupScreen;
        CommandersScreen commandersScreen;
        ResultsScreen resultsScreen;
        SettingsScreen settingsScreen;
        Screen current = Screen.Main, commandersReturn = Screen.Main, pending = Screen.Main;

        public Screen Current => current;
        public MainMenu MainMenuScreen => mainMenu;
        public SkirmishSetup SetupScreen => setupScreen;
        public CommandersScreen CommandersView => commandersScreen;
        public ResultsScreen ResultsView => resultsScreen;
        public SettingsScreen SettingsView => settingsScreen;
        public VisualElement Root => root;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Instance = null;

        /// <summary>Playing MainMenu.unity (or an empty Boot.unity) directly still gets a GameFlow.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoCreate()
        {
            string s = SceneManager.GetActiveScene().name;
            if (Instance == null && (s == BootScene || s == MenuScene))
                new GameObject("GameFlow").AddComponent<GameFlow>();
        }

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);
            LoadProfile();
            BuildUi();
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            if (Instance != this) return;
            Instance = null;
            BattleSession.Finished = null;
            BattleSession.Pending = null;
        }

        void Start()
        {
            string s = SceneManager.GetActiveScene().name;
            if (s == MenuScene) Show(Screen.Main);
            else if (s == BattleScene) ShowUi(false);
            else GoToMenu(Screen.Main);        // Boot (or any other scene): open the menu
        }

        // ---------------------------------------------------------------- profile (group 3: saves)
        /// <summary>Reads settings.json (applies it, key bindings included) and save.json (Commander, last setup, records).</summary>
        void LoadProfile()
        {
            GameData.Reload();
            Settings.Load();                    // also hooks and loads the key bindings (Hotkeys.LoadHook / SaveHook)
            Settings.Apply();
            save = SaveData.Load();
            setup = save.SetupConfig();
            var ids = Battle.LeaderIds();
            commander = ids.Contains(save.commander) ? save.commander : setup.leader;
            Settings.HintsTurnedOn = () =>
            {
                var g = Instance;
                if (g == null || g.save == null) return;
                g.save.MarkHintsSeen(HintsSkirmish, false);
                g.save.Write();
            };
        }

        /// <summary>The save's hints_seen id of the first-match hints.</summary>
        public const string HintsSkirmish = "skirmish";

        /// <summary>Should a new Skirmish show the first-match hints? (setting on and not finished before)</summary>
        public bool WantsHints(BattleConfig c) => save != null && c != null && c.mode == "skirmish" && Settings.ShowHints && !save.HintsSeen(HintsSkirmish);

        /// <summary>The first-match hints were finished or hidden: remember it in save.json.</summary>
        public void HintsDone()
        {
            if (save == null) return;
            save.MarkHintsSeen(HintsSkirmish);
            save.Write();
        }

        /// <summary>Writes save.json: the Commander and the last setup (records are added by RecordResult).</summary>
        public void SaveProfile()
        {
            if (save == null) return;
            save.commander = commander;
            if (setup != null) save.StoreSetup(setup);
            save.Write();
        }

        /// <summary>Adds the result to the per-map record (wins, losses, best time per map + difficulty + size, as
        /// rts.py record(); a battle left from the pause menu is not counted) and returns the results screen's line.</summary>
        public string RecordResult(BattleResult r)
        {
            if (save == null || r == null) return null;
            var rec = save.AddResult(r);
            return SaveData.RecordText(rec ?? save.RecordFor(r.config));
        }

        /// <summary>The record line the setup screen shows for a map + difficulty + size (null = none yet).</summary>
        public string RecordLine(BattleConfig c) => save == null ? null : SaveData.RecordText(save.RecordFor(c));

        // ---------------------------------------------------------------- UI
        void BuildUi()
        {
            root = Ui.Document(transform, "Menus", 10);
            mainMenu = new MainMenu(true);
            mainMenu.onSettings = () => Show(Screen.Settings);
            mainMenu.onSkirmish = () => Show(Screen.Setup);
            mainMenu.onCommanders = () => OpenCommanders(Screen.Main);
            mainMenu.onQuit = QuitGame;

            setupScreen = new SkirmishSetup();
            setupScreen.onBack = () => { setup = setupScreen.Config.Clone(); Show(Screen.Main); };
            setupScreen.onCommanders = () => OpenCommanders(Screen.Setup);
            setupScreen.onDeploy = StartSkirmish;
            setupScreen.recordLine = RecordLine;

            commandersScreen = new CommandersScreen();
            commandersScreen.onBack = () => Show(commandersReturn, false);
            commandersScreen.onSelect = id =>
            {
                commander = id;
                setup.leader = id;
                setupScreen.SetLeader(id);
                SaveProfile();
                Show(commandersReturn, false);
            };

            resultsScreen = new ResultsScreen();
            resultsScreen.onRematch = () => { if (lastResult?.config != null) StartSkirmish(lastResult.config); };
            resultsScreen.onSetup = () => Show(Screen.Setup);
            resultsScreen.onMenu = () => Show(Screen.Main);

            settingsScreen = new SettingsScreen(Settings.Model());
            settingsScreen.onBack = () => Show(Screen.Main);

            foreach (var e in new[] { mainMenu.root, setupScreen.root, commandersScreen.root, resultsScreen.root, settingsScreen.root })
            {
                root.Add(e);
                Ui.Show(e, false);
            }
        }

        void ShowUi(bool on) => Ui.Show(root, on);

        void OpenCommanders(Screen back)
        {
            commandersReturn = back;
            Show(Screen.Commanders);
        }

        /// <summary>Shows one menu screen (in the menu scene). reopen: the setup screen starts again from the last setup
        /// (false when coming back from the Commanders screen, so its unsaved picks stay).</summary>
        public void Show(Screen s, bool reopen = true)
        {
            current = s;
            ShowUi(true);
            Ui.Show(mainMenu.root, s == Screen.Main);
            Ui.Show(setupScreen.root, s == Screen.Setup);
            Ui.Show(commandersScreen.root, s == Screen.Commanders);
            Ui.Show(resultsScreen.root, s == Screen.Results);
            Ui.Show(settingsScreen.root, s == Screen.Settings);
            if (s == Screen.Main) mainMenu.Refresh(GameData.S(GameData.Commanders[commander], "name", commander));
            else if (s == Screen.Setup && reopen) setupScreen.Open(setup);
            else if (s == Screen.Commanders) commandersScreen.Open(commander);
            else if (s == Screen.Settings) settingsScreen.Open();
        }

        // ---------------------------------------------------------------- scenes
        /// <summary>Loads the menu scene and opens a screen there.</summary>
        public void GoToMenu(Screen s)
        {
            Mode = "menu";
            pending = s;
            ShowUi(false);
            SceneManager.LoadScene(MenuScene);
        }

        void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (scene.name == MenuScene) Show(pending);
            else if (scene.name == BattleScene) ShowUi(false);
        }

        /// <summary>DEPLOY / REMATCH: remembers the setup and loads Battle.unity with it.</summary>
        public void StartSkirmish(BattleConfig cfg)
        {
            setup = cfg.Clone();
            setup.seed = 0;
            commander = setup.leader;
            SaveProfile();
            Mode = setup.mode = "skirmish";
            var run = setup.Clone();
            BattleSession.Pending = run;
            BattleSession.Finished = OnBattleFinished;
            battlesStarted++;
            ShowUi(false);
            SceneManager.LoadScene(BattleScene);
        }

        void OnBattleFinished(BattleResult r)
        {
            lastResult = r;
            string line = RecordResult(r);
            SaveProfile();
            resultsScreen.Show(r, line);
            GoToMenu(Screen.Results);
        }

        public void QuitGame()
        {
            SaveProfile();
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        void Update()
        {
            if (Mode != "menu") return;
            PortraitLibrary.Tick();
            if (current == Screen.Setup) setupScreen.Tick();
            else if (current == Screen.Commanders) commandersScreen.Tick();
            else if (current == Screen.Settings) settingsScreen.Tick();
            if (Outpost.View.Hotkeys.PressedAny("menu_back"))
            {
                if (current == Screen.Setup) setupScreen.onBack();
                else if (current == Screen.Commanders) Show(commandersReturn, false);
                else if (current == Screen.Results) Show(Screen.Main);
                else if (current == Screen.Settings) settingsScreen.Back();
            }
            else if (Outpost.View.Hotkeys.PressedAny("menu_confirm"))
            {
                if (current == Screen.Setup) setupScreen.Deploy();
                else if (current == Screen.Commanders) commandersScreen.Select();
            }
            if (current == Screen.Commanders && (Outpost.View.Hotkeys.PressedAny("menu_up") || Outpost.View.Hotkeys.PressedAny("menu_down")))
                commandersScreen.Step(Outpost.View.Hotkeys.PressedAny("menu_up") ? -1 : 1);
        }
    }
}
