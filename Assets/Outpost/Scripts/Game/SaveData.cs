// SaveData.cs · roadmap step M2 (filled Nov 21, Milestone 2 group 3).
// The save file: records, unlocks, Commander, loadout, campaign, run in progress.

using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using Outpost.Sim;

namespace Outpost.Game
{
    /// <summary>Wins, losses and best time for one map + difficulty (+ size), as Python save["command"][key].</summary>
    public class SkirmishRecord
    {
        public int wins, losses;
        /// <summary>Fastest win in whole seconds (Python round(sim.time)); null = no win yet.</summary>
        public int? best_time;
    }

    /// <summary>The last Skirmish setup, as Python save["command_setup"] (same key names).</summary>
    public class SetupSave
    {
        public string map_id, difficulty, leader, size;
        public int? seed;
        public List<string> loadout;
    }

    /// <summary>Campaign progress, as Python save["command_campaign"] = {done: {op id: {bonus, difficulty, time}}}. Filled from M3.</summary>
    public class CampaignProgress
    {
        public Dictionary<string, JObject> done = new Dictionary<string, JObject>();
    }

    /// <summary>
    /// save.json in Application.persistentDataPath. The key names follow the Python save.json so the two stay easy to
    /// compare: commander, command_setup (last Skirmish setup: map, difficulty, leader, loadout, size), command (records
    /// per map:difficulty[:size]), command_campaign, rts_tech, salvage, rts_run / rts_runs (Siege Run, M3).
    /// Fields this version doesn't know are kept as they are (extra) and written back.
    /// Writing is safe: the new file goes to save.json.tmp first, then replaces save.json, and the previous save
    /// becomes save.json.bak. Loading falls back to the backup, then to defaults, and never throws; a file that could
    /// not be read is kept as save.json.corrupt.
    /// Headless matches and rules tests never touch it (only GameFlow loads and writes the save); tests set Dir.
    /// </summary>
    public class SaveData
    {
        public const int CurrentVersion = 1;
        public const string FileName = "save.json";

        public int version = CurrentVersion;
        /// <summary>The Commander used in every mode (Python save["commander"]).</summary>
        public string commander = "vanguard";
        public SetupSave command_setup;
        public Dictionary<string, SkirmishRecord> command = new Dictionary<string, SkirmishRecord>();
        public CampaignProgress command_campaign = new CampaignProgress();
        /// <summary>Tech Lab ranks (M3) and the Salvage that buys them.</summary>
        public Dictionary<string, int> rts_tech = new Dictionary<string, int>();
        public int salvage;
        /// <summary>The Siege Run in progress (M3; null = none), Siege Run stats and its last difficulty.</summary>
        public JObject rts_run, rts_runs;
        public string rts_run_difficulty;
        /// <summary>Command Modes picks and bests (M4).</summary>
        public JObject command_modes, command_bests;
        /// <summary>Help shown once and finished (M2 group 4): "skirmish" = the first-match hints.</summary>
        public List<string> hints_seen = new List<string>();
        /// <summary>When it was last written (UTC, ISO 8601).</summary>
        public string saved_at;

        [JsonExtensionData] public IDictionary<string, JToken> extra;

        // ------------------------------------------------------------------ file
        /// <summary>Folder of save.json and settings.json (null = Application.persistentDataPath). Tests point it elsewhere.</summary>
        public static string Dir;
        public static string Folder => string.IsNullOrEmpty(Dir) ? Application.persistentDataPath : Dir;
        public static string PathOf => Path.Combine(Folder, FileName);
        /// <summary>Where the last Load came from: main, backup, new (no file yet) or defaults (unreadable file, no backup).</summary>
        public static string LastLoadSource { get; private set; } = "none";

        public static SaveData Load()
        {
            string path = PathOf, source;
            var d = SafeJson.Load<SaveData>(path, out source);
            LastLoadSource = source;
            return (d ?? new SaveData()).Normalize();
        }

        /// <summary>Writes save.json safely (temp file + replace, previous save kept as .bak). False if it failed.</summary>
        public bool Write()
        {
            Normalize();
            saved_at = DateTime.UtcNow.ToString("o");
            return SafeJson.Write(PathOf, this);
        }

        /// <summary>Fills anything missing (old or hand-edited files) and upgrades older versions.</summary>
        public SaveData Normalize()
        {
            if (command == null) command = new Dictionary<string, SkirmishRecord>();
            var bad = new List<string>();
            foreach (var kv in command) if (kv.Value == null) bad.Add(kv.Key);
            foreach (var k in bad) command.Remove(k);
            if (command_campaign == null) command_campaign = new CampaignProgress();
            if (command_campaign.done == null) command_campaign.done = new Dictionary<string, JObject>();
            if (rts_tech == null) rts_tech = new Dictionary<string, int>();
            if (hints_seen == null) hints_seen = new List<string>();
            if (string.IsNullOrEmpty(commander)) commander = "vanguard";
            if (version < CurrentVersion) version = CurrentVersion;     // nothing to migrate yet (version 1 is the first)
            return this;
        }

        // ------------------------------------------------------------------ help
        public bool HintsSeen(string id) => hints_seen != null && hints_seen.Contains(id);

        public void MarkHintsSeen(string id, bool seen = true)
        {
            Normalize();
            if (seen && !hints_seen.Contains(id)) hints_seen.Add(id);
            if (!seen) hints_seen.Remove(id);
        }

        // ------------------------------------------------------------------ Skirmish setup
        /// <summary>The setup screen's starting point: the saved setup, else defaults with the saved Commander.</summary>
        public BattleConfig SetupConfig()
        {
            var c = new BattleConfig();
            var s = command_setup;
            if (s != null)
            {
                if (!string.IsNullOrEmpty(s.map_id)) c.mapId = s.map_id;
                if (!string.IsNullOrEmpty(s.difficulty)) c.difficulty = s.difficulty;
                if (!string.IsNullOrEmpty(s.size)) c.size = s.size;
                if (s.loadout != null) c.loadout = new List<string>(s.loadout);
            }
            c.leader = s != null && !string.IsNullOrEmpty(s.leader) ? s.leader : commander;
            return c.Validated();
        }

        public void StoreSetup(BattleConfig c)
        {
            if (c == null) return;
            command_setup = new SetupSave
            {
                map_id = c.mapId, difficulty = c.difficulty, leader = c.leader, size = c.size,
                seed = c.seed == 0 ? (int?)null : c.seed,
                loadout = c.loadout == null ? null : new List<string>(c.loadout),
            };
        }

        // ------------------------------------------------------------------ records
        /// <summary>rts.py record key: map:difficulty, plus :size unless the size is small.</summary>
        public static string RecordKey(string mapId, string difficulty, string size) =>
            mapId + ":" + difficulty + (string.IsNullOrEmpty(size) || size == "small" ? "" : ":" + size);

        public SkirmishRecord RecordFor(BattleConfig c)
        {
            if (c == null) return null;
            SkirmishRecord r;
            return command.TryGetValue(RecordKey(c.mapId, c.difficulty, c.size), out r) ? r : null;
        }

        /// <summary>Adds a finished Skirmish to its record (rts.py record()). A battle left from the pause menu has no
        /// winner and is not counted (Python only records when the battle has a winner). Returns the record, or null.</summary>
        public SkirmishRecord AddResult(BattleResult res)
        {
            if (res == null || res.config == null || res.Left) return null;
            if (res.config.mode != null && res.config.mode != "skirmish") return null;
            string key = RecordKey(res.config.mapId, res.config.difficulty, res.config.size);
            SkirmishRecord r;
            if (!command.TryGetValue(key, out r) || r == null) command[key] = r = new SkirmishRecord();
            if (res.Won)
            {
                r.wins++;
                int t = Mathf.RoundToInt(res.time);
                if (r.best_time == null || t < r.best_time.Value) r.best_time = t;
            }
            else r.losses++;
            return r;
        }

        /// <summary>Record here: 3W 1L  best 12:04 (rts.py setup screen), or null without a record.</summary>
        public static string RecordText(SkirmishRecord r)
        {
            if (r == null) return null;
            string best = r.best_time.HasValue ? $"  best {r.best_time.Value / 60}:{r.best_time.Value % 60:00}" : "";
            return $"Record here: {r.wins}W {r.losses}L{best}";
        }

        // ------------------------------------------------------------------ unlocks (used from M3)
        /// <summary>The last campaign operation that may be played (rts_campaign.unlocked_index): all with Unlock all
        /// (a gameplay setting, on by default as in Python), else the one after the last finished operation.</summary>
        public int CampaignUnlocked(IList<string> missionIds, bool unlockAll)
        {
            if (missionIds == null || missionIds.Count == 0) return 0;
            if (unlockAll) return missionIds.Count - 1;
            int n = 0;
            for (int i = 0; i < missionIds.Count; i++) if (command_campaign.done.ContainsKey(missionIds[i])) n = i + 1;
            return Mathf.Min(n, missionIds.Count - 1);
        }

        public int TechRank(string id)
        {
            int r;
            return rts_tech.TryGetValue(id, out r) ? r : 0;
        }
    }

    /// <summary>Reads and writes JSON files the safe way (shared by SaveData and Settings).</summary>
    public static class SafeJson
    {
        static readonly JsonSerializerSettings Tolerant = new JsonSerializerSettings
        {
            // a field with the wrong type is skipped (keeps its default) instead of losing the whole file
            Error = (s, e) => { e.ErrorContext.Handled = true; },
            MissingMemberHandling = MissingMemberHandling.Ignore,
            NullValueHandling = NullValueHandling.Include,
        };

        /// <summary>Reads path, else path.bak. source = main / backup / new / defaults. Never throws; null = use defaults.
        /// A main file that could not be read is copied to path.corrupt first.</summary>
        public static T Load<T>(string path, out string source) where T : class
        {
            T v = TryRead<T>(path);
            if (v != null) { source = "main"; return v; }
            bool existed = File.Exists(path);
            if (existed)
            {
                try { File.Copy(path, path + ".corrupt", true); } catch (Exception) { }
                Debug.LogWarning($"[Outpost] {Path.GetFileName(path)} could not be read; trying the backup (the bad file is kept as {Path.GetFileName(path)}.corrupt).");
            }
            v = TryRead<T>(path + ".bak");
            if (v != null) { source = "backup"; return v; }
            if (existed) Debug.LogWarning($"[Outpost] No usable backup of {Path.GetFileName(path)}: starting from defaults.");
            source = existed ? "defaults" : "new";
            return null;
        }

        public static T TryRead<T>(string path) where T : class
        {
            try
            {
                if (!File.Exists(path)) return null;
                string text = File.ReadAllText(path);
                if (string.IsNullOrWhiteSpace(text)) return null;
                var tok = JToken.Parse(text);
                if (tok.Type != JTokenType.Object) return null;
                return JsonConvert.DeserializeObject<T>(text, Tolerant);
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>Writes value as indented JSON: path.tmp first, then it replaces path and the old file becomes path.bak.</summary>
        public static bool Write(string path, object value)
        {
            string tmp = path + ".tmp", bak = path + ".bak";
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(tmp, JsonConvert.SerializeObject(value, Formatting.Indented));
                if (File.Exists(path))
                {
                    try { File.Replace(tmp, path, bak); }
                    catch (Exception)
                    {
                        // some file systems can't replace: copy the old file to .bak, then move the new one in
                        File.Copy(path, bak, true);
                        File.Copy(tmp, path, true);
                        File.Delete(tmp);
                    }
                }
                else File.Move(tmp, path);
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Outpost] Could not write {path}: {e.Message}");
                return false;
            }
        }
    }
}
