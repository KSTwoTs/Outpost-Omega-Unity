using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;
using Outpost.Content;

namespace Outpost.Sim
{
    /// <summary>
    /// Stealth and detection: who is hidden from whom, every detector of a faction, and the Sensor Array Scan.
    /// </summary>
    public partial class Battle
    {
        // ------------------------------------------------------------------ stealth and detection
        public static bool Stealthed(Entity e) => e is Unit u && (u.stealth || u.Has("camo"));

        /// <summary>True when the faction can't see or target e: burrowed, or cloaked and not detected.</summary>
        public bool HiddenFrom(Entity e, string faction)
        {
            if (!(e is Unit u) || u.faction == faction) return false;
            if (u.Has("burrowed")) return true;
            return Stealthed(u) && !(detected.TryGetValue(faction, out var seen) && seen.Contains(u.id));
        }

        /// <summary>Everything of a faction that reveals cloaked enemies, as (x, y, radius).</summary>
        List<Vector3> Detectors(string faction)
        {
            var cfg = GameData.Rts["detection"];
            float near = GameData.F(cfg, "near", 80);
            var res = new List<Vector3>();
            foreach (var u in units)
            {
                if (!u.alive || u.faction != faction) continue;
                float r = u.detect;
                if (u.kind == "commander") r = Mathf.Max(r, GameData.F(cfg, "commander", 200));
                else if (u.kind == "queen") r = Mathf.Max(r, GameData.F(cfg, "queen", 240));
                res.Add(new Vector3(u.x, u.y, Mathf.Max(r, near)));
            }
            foreach (var b in buildings)
            {
                if (!b.alive || b.faction != faction || !b.Done) continue;
                float r = GameData.F(b.data, "detect", b.kind == "hive" ? GameData.F(cfg, "hive", 200) : 0);
                if (r > 0) res.Add(new Vector3(b.x, b.y, r));
            }
            foreach (var t in towers)
            {
                if (!t.alive || t.faction != faction || !t.Done) continue;
                if (t.kind == "spine") res.Add(new Vector3(t.x, t.y, GameData.F(cfg, "spine", 180)));
                else if (t.behavior == "support") res.Add(new Vector3(t.x, t.y, t.range * GameData.F(cfg, "radar", 1f) + 60));   // Radar towers
            }
            if (faction == "player")
                foreach (var s in scans) res.Add(new Vector3(s.pos.x, s.pos.y, s.r));
            return res;
        }

        void UpdateDetection()
        {
            foreach (var kv in detected)
            {
                kv.Value.Clear();
                List<Vector3> dets = null;
                foreach (var u in units)
                {
                    if (!u.alive || u.faction == kv.Key || !Stealthed(u)) continue;
                    if (dets == null) dets = Detectors(kv.Key);
                    foreach (var d in dets)
                        if (D2(u.x, u.y, d.x, d.y) <= d.z * d.z)
                        {
                            kv.Value.Add(u.id);
                            break;
                        }
                }
            }
        }

        /// <summary>Sensor Array: reveals cloaked units around a spot anywhere on the map for a while.</summary>
        public string Scan(Building b, Vector2 at)
        {
            var sc = b.data?["scan"];
            if (sc == null || !b.Done) return "Can't scan";
            if (b.scanCd > 0) return $"Recharging ({(int)b.scanCd} s)";
            b.scanCd = GameData.F(sc, "cooldown", 45);
            float r = GameData.F(sc, "radius", 260);
            scans.Add(new ScanArea { pos = at, r = r, t = GameData.F(sc, "time", 12) });
            fx?.Zone(at, r, GameData.F(sc, "time", 12), "scan");
            UpdateFog();
            UpdateDetection();
            return null;
        }
    }
}
