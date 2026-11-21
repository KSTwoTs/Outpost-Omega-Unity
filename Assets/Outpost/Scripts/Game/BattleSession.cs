// BattleSession.cs · roadmap step M2 (filled Nov 21, Milestone 2 group 1).
// Takes a BattleConfig, creates the Battle, BattleView and HUD, runs the clock (replaces SkirmishBootstrap).

using UnityEngine;
using Outpost.Content;
using Outpost.Sim;
using Outpost.UI;
using Outpost.View;

namespace Outpost.Game
{
    /// <summary>
    /// One battle in Battle.unity. GameFlow sets Pending (the config) and Finished (where the result goes) before it
    /// loads the scene; when Battle.unity is played directly in the editor there is no GameFlow, so the inspector
    /// fields make a default Skirmish and the end screen offers Play again instead of Continue.
    /// Every frame it ticks the Battle (Sim), the Sim reports through IBattleFx, the HUD and views read it.
    /// </summary>
    public class BattleSession : MonoBehaviour
    {
        [Header("Used when Battle.unity is played directly")]
        public string mapId = "dune_sea";
        public string difficulty = "Normal";
        [Tooltip("vanguard, engineer, tactician, ignis, rime, bastion, kestrel, ledger or hawk")]
        public string leader = "vanguard";
        [Tooltip("Multiplies game speed (handy for testing).")]
        public float gameSpeed = 1f;

        /// <summary>The next battle to start (GameFlow sets it before loading Battle.unity; taken once).</summary>
        public static BattleConfig Pending;
        /// <summary>Where the result goes when the player continues from the end screen or quits (GameFlow).</summary>
        public static System.Action<BattleResult> Finished;
        /// <summary>The running session (null outside Battle.unity).</summary>
        public static BattleSession Current { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Pending = null;
            Finished = null;
            Current = null;
        }

        BattleConfig config;
        Battle battle;
        BattleView view;
        HudRoot hud;
        bool reported;

        public Battle Battle => battle;
        public BattleConfig Config => config;
        public BattleView View => view;
        public HudRoot Hud => hud;
        public CommandInput Input => view != null ? view.input : null;

        void Awake() => Current = this;

        void OnDestroy()
        {
            if (Current == this) Current = null;
        }

        void Start()
        {
            view = gameObject.AddComponent<BattleView>();
            view.Setup();
            hud = gameObject.AddComponent<HudRoot>();
            Settings.EnsureLoaded();            // Battle.unity played directly: no GameFlow loaded them
            hud.SetSettings(new SettingsScreen(Settings.Model()));
            if (MapBuilder.Available().Length == 0)
            {
                Debug.LogError("[Outpost] No exported maps in StreamingAssets/Outpost/maps. Run tools/export_unity.py.");
                return;
            }
            var cfg = Pending ?? new BattleConfig { mapId = mapId, difficulty = difficulty, leader = leader };
            Pending = null;
            Begin(cfg);
        }

        /// <summary>Starts (or restarts) a battle from a config.</summary>
        public void Begin(BattleConfig cfg)
        {
            GameData.Reload();
            config = cfg.Validated();
            reported = false;
            var map = MapBuilder.Load(config.mapId);
            var fx = view.BuildWorld(map);
            battle = config.Create(map, fx, view.Bind);
            view.Started(battle);
            if (view.input != null) view.input.speed = Settings.StartSpeed;
            Settings.ApplyCamera(view.rig);
            hud.Bind(battle, view.input, fx, view.rig, map, MapBuilder.LastAlbedo, EndButton);
            bool flow = Finished != null;
            hud.endButtonText = flow ? "Continue" : "Play again";
            hud.quit = flow ? (System.Action)Leave : null;
            hud.restartFromPause = Restart;
            // first-match hints: only through the game flow (the save remembers them), first Skirmish, setting on
            var flowNow = GameFlow.Instance;
            if (flowNow != null && flowNow.WantsHints(config)) hud.hints.Begin(battle, view.input, flowNow.HintsDone);
            fx.Message("player", "Build up and destroy the alien Hive. Select your Command Post to build; claim the yellow pads for more bases.");
        }

        /// <summary>The end screen's button: results (game flow) or a fresh battle with the same setup (played directly).</summary>
        void EndButton()
        {
            if (Finished != null) Report();
            else
            {
                var again = config.Clone();
                again.seed = 0;
                Begin(again);
            }
        }

        /// <summary>Pause menu RESTART: the same setup again with a new seed (not recorded, like leaving).</summary>
        public void Restart()
        {
            if (config == null) return;
            var again = config.Clone();
            again.seed = 0;
            Begin(again);
        }

        /// <summary>Pause menu: leave the battle. The result has no winner, so it is not added to the map record (as Python).</summary>
        public void Leave()
        {
            if (battle == null) return;
            Report();
        }

        /// <summary>Hands the result to GameFlow once (it loads the menu scene with the results screen).</summary>
        public void Report()
        {
            if (reported || battle == null) return;
            reported = true;
            var r = BattleResult.From(battle, config);
            var done = Finished;
            Finished = null;
            done?.Invoke(r);
        }

        void Update()
        {
            if (battle == null || hud.paused) return;
            float dt = Mathf.Min(Time.deltaTime, 0.05f) * gameSpeed * (view.input != null ? view.input.speed : 1f);
            // big speed-ups run in several steps so fights stay stable
            int steps = Mathf.Max(1, Mathf.CeilToInt(dt / 0.05f));
            view.UpdateViewRect();
            for (int i = 0; i < steps; i++) battle.Tick(dt / steps);
        }
    }
}
