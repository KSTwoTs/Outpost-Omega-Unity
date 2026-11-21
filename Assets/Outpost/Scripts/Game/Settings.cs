// Settings.cs · roadmap step M2 (filled Nov 21, Milestone 2 group 3).
// Video, audio, key bindings, gameplay: settings.json, applying them, and the model the settings screen edits.

using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using Outpost.UI;
using Outpost.View;

namespace Outpost.Game
{
    public class VideoSettings
    {
        /// <summary>0 x 0 = the desktop resolution.</summary>
        public int width, height;
        /// <summary>fullscreen (exclusive), borderless (full-screen window) or windowed.</summary>
        public string mode = "borderless";
        public bool vsync = true;
        /// <summary>Index into QualitySettings.names; -1 = leave the project's level.</summary>
        public int quality = -1;
        /// <summary>Menus and HUD size (1 = the 1920x1080 layout scaled to the screen height).</summary>
        public float uiScale = 1f;
    }

    /// <summary>Volumes 0-100 (Python sfx 80, music 50). Stored now; sound itself is Milestone 4.</summary>
    public class AudioSettings
    {
        public int master = 100, music = 50, effects = 80;
    }

    public class GameplaySettings
    {
        /// <summary>Game speed a battle starts at (Tab still cycles x1 / x1.5 / x2).</summary>
        public float gameSpeed = 1f;
        public bool edgeScroll = true;
        /// <summary>Camera pan speed multiplier.</summary>
        public float cameraSpeed = 1f;
        /// <summary>Python setting unlock_all (on by default): every campaign operation is open. Used from M3.</summary>
        public bool unlockAll = true;
        /// <summary>First-match hints in the first Skirmish (M2 group 4). Turning it back on shows them again.</summary>
        public bool showHints = true;
    }

    /// <summary>settings.json (next to save.json). controls = the rebinds that differ from the defaults (Hotkeys.ToJson).</summary>
    public class SettingsData
    {
        public const int CurrentVersion = 1;
        public int version = CurrentVersion;
        public VideoSettings video = new VideoSettings();
        public AudioSettings audio = new AudioSettings();
        public GameplaySettings gameplay = new GameplaySettings();
        public JObject controls = new JObject();
        [JsonExtensionData] public IDictionary<string, JToken> extra;

        public SettingsData Clone() => JsonConvert.DeserializeObject<SettingsData>(JsonConvert.SerializeObject(this)).Normalize();

        public SettingsData Normalize()
        {
            if (video == null) video = new VideoSettings();
            if (audio == null) audio = new AudioSettings();
            if (gameplay == null) gameplay = new GameplaySettings();
            if (controls == null) controls = new JObject();
            if (string.IsNullOrEmpty(video.mode) || System.Array.IndexOf(Settings.ModeIds, video.mode) < 0) video.mode = "borderless";
            video.uiScale = Mathf.Clamp(video.uiScale <= 0 ? 1f : video.uiScale, 0.5f, 2f);
            audio.master = Mathf.Clamp(audio.master, 0, 100);
            audio.music = Mathf.Clamp(audio.music, 0, 100);
            audio.effects = Mathf.Clamp(audio.effects, 0, 100);
            gameplay.gameSpeed = Mathf.Clamp(gameplay.gameSpeed <= 0 ? 1f : gameplay.gameSpeed, 0.5f, 3f);
            gameplay.cameraSpeed = Mathf.Clamp(gameplay.cameraSpeed <= 0 ? 1f : gameplay.cameraSpeed, 0.25f, 3f);
            if (version < CurrentVersion) version = CurrentVersion;
            return this;
        }
    }

    /// <summary>
    /// The player's settings. Load() reads settings.json (backup / defaults if it can't), hooks the key bindings to it
    /// (Hotkeys.LoadHook / SaveHook) and Apply() uses them: resolution and window mode (in a build; the editor's game
    /// view can't be resized from code), vsync, quality level, UI scale, master volume (AudioListener), camera speed and
    /// edge scrolling on the battle camera. The default game speed is used when a battle starts (BattleSession).
    /// Model() is what the settings screen edits.
    /// </summary>
    public static class Settings
    {
        public const string FileName = "settings.json";
        public static readonly string[] ModeIds = { "fullscreen", "borderless", "windowed" };
        public static readonly string[] ModeNames = { "Fullscreen", "Borderless window", "Windowed" };
        public static readonly float[] Speeds = { 1f, 1.5f, 2f };
        public static readonly float[] CameraSpeeds = { 0.5f, 0.75f, 1f, 1.25f, 1.5f, 2f };
        public static readonly float[] UiScales = { 0.8f, 0.9f, 1f, 1.1f, 1.2f, 1.3f };
        public static readonly int[] Volumes = { 0, 10, 20, 30, 40, 50, 60, 70, 80, 90, 100 };
        /// <summary>RtsCamera.panSpeed at camera speed x1.</summary>
        public const float BasePanSpeed = 60f;

        public static SettingsData Current { get; private set; }
        public static string LastLoadSource { get; private set; } = "none";
        /// <summary>What the last Apply set, for checks: e.g. 1920x1080 borderless vsync 1 quality 2 ui 1.</summary>
        public static string LastApplied { get; private set; } = "";
        public static string PathOf => System.IO.Path.Combine(SaveData.Folder, FileName);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { Current = null; }

        /// <summary>Loads and applies the settings once (Battle.unity played without the menu).</summary>
        public static void EnsureLoaded()
        {
            if (Current != null) return;
            Load();
            Apply();
        }

        public static void Load()
        {
            string source;
            Current = (SafeJson.Load<SettingsData>(PathOf, out source) ?? new SettingsData()).Normalize();
            LastLoadSource = source;
            Hotkeys.LoadHook = () => Current?.controls?.ToString(Formatting.None);
            Hotkeys.SaveHook = json =>
            {
                if (Current == null) return;
                try { Current.controls = JObject.Parse(string.IsNullOrEmpty(json) ? "{}" : json); }
                catch (System.Exception) { Current.controls = new JObject(); }
                Save();
            };
            Hotkeys.Load();
        }

        public static bool Save() => Current != null && SafeJson.Write(PathOf, Current);

        /// <summary>Uses the current settings now.</summary>
        public static void Apply()
        {
            var s = Current;
            if (s == null) return;
            var v = s.video;
            int mi = System.Array.IndexOf(ModeIds, v.mode);
            var mode = mi == 0 ? FullScreenMode.ExclusiveFullScreen : mi == 2 ? FullScreenMode.Windowed : FullScreenMode.FullScreenWindow;
            int w = v.width, h = v.height;
            if (w <= 0 || h <= 0) { w = Screen.currentResolution.width; h = Screen.currentResolution.height; }
            if (!Application.isEditor && (Screen.width != w || Screen.height != h || Screen.fullScreenMode != mode))
                Screen.SetResolution(w, h, mode);
            QualitySettings.vSyncCount = v.vsync ? 1 : 0;
            if (v.quality >= 0 && v.quality < QualitySettings.names.Length && v.quality != QualitySettings.GetQualityLevel())
                QualitySettings.SetQualityLevel(v.quality, true);
            Ui.SetScale(v.uiScale);
            AudioListener.volume = s.audio.master / 100f;
            ApplyCamera(RtsCamera.Active);
            LastApplied = $"{w}x{h} {v.mode} vsync {QualitySettings.vSyncCount} quality {QualitySettings.GetQualityLevel()} ui {Ui.Scale}";
        }

        public static void ApplyCamera(RtsCamera rig)
        {
            if (rig == null || Current == null) return;
            rig.edgePan = Current.gameplay.edgeScroll;
            rig.panSpeed = BasePanSpeed * Current.gameplay.cameraSpeed;
        }

        /// <summary>The speed a battle starts at.</summary>
        public static float StartSpeed => Current != null ? Current.gameplay.gameSpeed : 1f;

        public static bool UnlockAll => Current == null || Current.gameplay.unlockAll;

        /// <summary>Are the first-match hints allowed (gameplay setting, on by default)?</summary>
        public static bool ShowHints => Current == null || Current.gameplay.showHints;

        /// <summary>Called when APPLY turns the hints setting from off to on (GameFlow then forgets that they were seen).</summary>
        public static System.Action HintsTurnedOn;

        /// <summary>What the settings screen edits (one model per screen).</summary>
        public static ISettingsModel Model() => new SettingsEditor();

        // ------------------------------------------------------------------ the screen's model
        class SettingsEditor : ISettingsModel
        {
            SettingsData work = new SettingsData();
            string savedControls = "{}";

            public void BeginEdit()
            {
                EnsureLoaded();
                work = Current.Clone();
                savedControls = Hotkeys.ToJson();
            }

            static string Key(SettingsData d) => JsonConvert.SerializeObject(new object[] { d.video, d.audio, d.gameplay });

            public bool Dirty => Current != null && (Key(work) != Key(Current) || Hotkeys.ToJson() != savedControls);

            public void Apply()
            {
                EnsureLoaded();
                Current.video = work.Clone().video;
                Current.audio = work.Clone().audio;
                bool hintsBefore = Current.gameplay.showHints;
                Current.gameplay = work.Clone().gameplay;
                Current.controls = JObject.Parse(Hotkeys.ToJson());
                Settings.Apply();
                Save();
                if (!hintsBefore && Current.gameplay.showHints) HintsTurnedOn?.Invoke();
                BeginEdit();
            }

            public void Revert()
            {
                EnsureLoaded();
                work = Current.Clone();
                if (Hotkeys.ToJson() != savedControls)
                {
                    Hotkeys.FromJson(savedControls);
                    Hotkeys.Save();
                }
            }

            static int Near(float[] list, float v)
            {
                int best = 0;
                for (int i = 1; i < list.Length; i++) if (Mathf.Abs(list[i] - v) < Mathf.Abs(list[best] - v)) best = i;
                return best;
            }

            static int Near(int[] list, int v)
            {
                int best = 0;
                for (int i = 1; i < list.Length; i++) if (Mathf.Abs(list[i] - v) < Mathf.Abs(list[best] - v)) best = i;
                return best;
            }

            static string[] Pcts(float[] l) => l.Select(x => Mathf.RoundToInt(x * 100) + "%").ToArray();

            static readonly string[] OffOn = { "Off", "On" };

            public List<SettingOption> Options()
            {
                var w = work;
                var list = new List<SettingOption>();
                // video
                var res = new List<Vector2Int> { Vector2Int.zero };
                foreach (var r in Screen.resolutions)
                {
                    var p = new Vector2Int(r.width, r.height);
                    if (!res.Contains(p)) res.Add(p);
                }
                var cur = new Vector2Int(w.video.width, w.video.height);
                if (!res.Contains(cur)) res.Add(cur);
                list.Add(new SettingOption
                {
                    tab = "VIDEO", id = "resolution", label = "Resolution",
                    note = Application.isEditor ? "Used in the built game (the editor's game view keeps its size)." : null,
                    choices = res.Select(r => r == Vector2Int.zero ? "Desktop (" + Screen.currentResolution.width + "x" + Screen.currentResolution.height + ")" : r.x + "x" + r.y).ToArray(),
                    get = () => Mathf.Max(0, res.IndexOf(new Vector2Int(w.video.width, w.video.height))),
                    set = i => { w.video.width = res[i].x; w.video.height = res[i].y; },
                });
                list.Add(new SettingOption
                {
                    tab = "VIDEO", id = "mode", label = "Display mode", choices = ModeNames, wrap = true,
                    get = () => Mathf.Max(0, System.Array.IndexOf(ModeIds, w.video.mode)),
                    set = i => w.video.mode = ModeIds[i],
                });
                list.Add(new SettingOption
                {
                    tab = "VIDEO", id = "vsync", label = "VSync", choices = OffOn, wrap = true,
                    get = () => w.video.vsync ? 1 : 0, set = i => w.video.vsync = i == 1,
                });
                list.Add(new SettingOption
                {
                    tab = "VIDEO", id = "quality", label = "Quality", choices = QualitySettings.names,
                    get = () => w.video.quality >= 0 ? w.video.quality : QualitySettings.GetQualityLevel(),
                    set = i => w.video.quality = i,
                });
                list.Add(new SettingOption
                {
                    tab = "VIDEO", id = "ui_scale", label = "UI scale", choices = Pcts(UiScales),
                    get = () => Near(UiScales, w.video.uiScale), set = i => w.video.uiScale = UiScales[i],
                });
                // audio
                string[] vols = Volumes.Select(x => x + "%").ToArray();
                list.Add(new SettingOption { tab = "AUDIO", id = "master", label = "Master volume", choices = vols, get = () => Near(Volumes, w.audio.master), set = i => w.audio.master = Volumes[i] });
                list.Add(new SettingOption { tab = "AUDIO", id = "music", label = "Music", choices = vols, get = () => Near(Volumes, w.audio.music), set = i => w.audio.music = Volumes[i] });
                list.Add(new SettingOption { tab = "AUDIO", id = "effects", label = "Sound effects", choices = vols, get = () => Near(Volumes, w.audio.effects), set = i => w.audio.effects = Volumes[i] });
                // gameplay
                list.Add(new SettingOption
                {
                    tab = "GAMEPLAY", id = "game_speed", label = "Starting game speed", note = "Tab still changes the speed during a battle.",
                    choices = Speeds.Select(x => "x" + x).ToArray(), wrap = true,
                    get = () => Near(Speeds, w.gameplay.gameSpeed), set = i => w.gameplay.gameSpeed = Speeds[i],
                });
                list.Add(new SettingOption
                {
                    tab = "GAMEPLAY", id = "edge_scroll", label = "Edge scrolling", note = "Move the camera by pushing the mouse against the screen edge.",
                    choices = OffOn, wrap = true, get = () => w.gameplay.edgeScroll ? 1 : 0, set = i => w.gameplay.edgeScroll = i == 1,
                });
                list.Add(new SettingOption
                {
                    tab = "GAMEPLAY", id = "camera_speed", label = "Camera speed", choices = Pcts(CameraSpeeds),
                    get = () => Near(CameraSpeeds, w.gameplay.cameraSpeed), set = i => w.gameplay.cameraSpeed = CameraSpeeds[i],
                });
                list.Add(new SettingOption
                {
                    tab = "GAMEPLAY", id = "unlock_all", label = "Unlock all campaign operations", note = "For the campaign (Milestone 3).",
                    choices = OffOn, wrap = true, get = () => w.gameplay.unlockAll ? 1 : 0, set = i => w.gameplay.unlockAll = i == 1,
                });
                list.Add(new SettingOption
                {
                    tab = "GAMEPLAY", id = "hints", label = "First-match hints", note = "Step-by-step tips in your first Skirmish. Turning them on again shows them in the next Skirmish.",
                    choices = OffOn, wrap = true, get = () => w.gameplay.showHints ? 1 : 0, set = i => w.gameplay.showHints = i == 1,
                });
                return list;
            }
        }
    }
}
