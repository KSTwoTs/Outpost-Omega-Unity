using System.IO;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Outpost.Sim
{
    /// <summary>The game's balance data (StreamingAssets/Outpost/data/*.json, copied from the Python game).</summary>
    public static class GameData
    {
        static JObject rts, towers;

        static string Dir => Path.Combine(Application.streamingAssetsPath, "Outpost", "data");

        public static JObject Rts => rts ??= JObject.Parse(File.ReadAllText(Path.Combine(Dir, "rts.json")));
        public static JObject Towers => towers ??= JObject.Parse(File.ReadAllText(Path.Combine(Dir, "towers.json")));

        public static JObject Unit(string kind) => Rts["units"]?[kind] as JObject;
        public static JObject Building(string kind) => Rts["buildings"]?[kind] as JObject;
        public static JObject Tower(string kind) => Towers[kind] as JObject;

        static JObject commanders, run;
        /// <summary>data/commanders.json: names, roles, bios, tips and the classic hero bonuses (Commanders screen).</summary>
        public static JObject Commanders => commanders ??= Load("commanders.json");
        /// <summary>data/run.json (the classic Siege Run; the Commanders screen reads hero_decks).</summary>
        public static JObject Run => run ??= Load("run.json");

        static JObject Load(string file)
        {
            var p = Path.Combine(Dir, file);
            return File.Exists(p) ? JObject.Parse(File.ReadAllText(p)) : new JObject();
        }

        /// <summary>Damage multiplier, attacker class -> target class (inf beats air, air beats veh, veh beats inf).</summary>
        public static float Counter(string attacker, string target)
        {
            var v = Rts["counters"]?[attacker]?[target];
            return v != null && v.Type != JTokenType.Null ? (float)v : 1f;
        }

        public static float F(JToken o, string key, float def = 0f)
        {
            var v = o?[key];
            return v == null || v.Type == JTokenType.Null ? def : (float)v;
        }

        public static int I(JToken o, string key, int def = 0)
        {
            var v = o?[key];
            return v == null || v.Type == JTokenType.Null ? def : (int)(float)v;
        }

        public static string S(JToken o, string key, string def = null)
        {
            var v = o?[key];
            return v == null || v.Type == JTokenType.Null ? def : (string)v;
        }

        public static bool B(JToken o, string key, bool def = false)
        {
            var v = o?[key];
            return v == null || v.Type == JTokenType.Null ? def : (bool)v;
        }

        public static string[] List(JToken o, string key)
        {
            if (!(o?[key] is JArray a)) return new string[0];
            var r = new string[a.Count];
            for (int i = 0; i < r.Length; i++) r[i] = (string)a[i];
            return r;
        }

        public static void Reload()
        {
            rts = null;
            towers = null;
            commanders = null;
            run = null;
        }
    }
}
