using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Outpost.Content;
using Outpost.View;

namespace Outpost.Sandbox
{
    /// <summary>
    /// Phase-1 look test: loads an exported battlefield, builds both main bases as connected complexes
    /// (platform, HQ / Hive, buildings, turrets), the expansion sites, relays and crates, and lines up
    /// both armies so you can fly around and judge the 3D look. No gameplay yet.
    /// </summary>
    public class SandboxBootstrap : MonoBehaviour
    {
        public string mapId = "dune_sea";
        public bool showArmies = true;

        static readonly string[] PlayerBuildings = { "barracks", "supply", "reactor", "depot", "airpad", "armory", "sensor" };
        static readonly string[] AlienBuildings = { "pool", "nest", "pit", "spire", "evo", "den", "node" };
        static readonly string[] PlayerTurrets = { "pulse", "missile", "tesla", "railgun" };
        static readonly string[] HumanArmy =
        {
            "marines", "rockets", "flamers", "medics", "snipers", "commandos", "commander", "buggy", "tank",
            "artillery", "hauler", "aa", "mantis", "mech", "hornet", "gunship", "sparrow", "swarm_drone"
        };
        static readonly string[] AlienArmy =
        {
            "skitters", "spitters", "brutes", "leeches", "burrowers", "rollers", "juggernaut", "grub", "wrecker",
            "ravager", "menders", "cloakers", "gliders", "bomber", "shield_drone", "carrier", "queen", "titan", "architect"
        };

        GameObject world;
        RtsCamera rig;
        MapData map;
        string[] maps;
        Vector3 playerBase, alienBase;

        void Start()
        {
            SetupLighting();
            SetupCamera();
            maps = MapBuilder.Available();
            if (maps.Length == 0)
            {
                Debug.LogError("[Outpost] No exported maps in StreamingAssets/Outpost/maps. Run tools/export_unity.py.");
                return;
            }
            if (System.Array.IndexOf(maps, mapId) < 0) mapId = maps[0];
            Build(mapId);
        }

        void SetupLighting()
        {
            var sunGo = GameObject.Find("Sun");
            if (sunGo == null) sunGo = new GameObject("Sun");
            var sun = sunGo.GetComponent<Light>();
            if (sun == null) sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(1f, 0.95f, 0.86f);
            sun.intensity = 1.6f;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.85f;
            // same sun as the 2D art: from the upper left of the screen, shadows fall down-right
            sunGo.transform.rotation = Quaternion.Euler(52f, 135f, 0f);
            RenderSettings.sun = sun;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.55f, 0.62f, 0.75f);
            RenderSettings.ambientEquatorColor = new Color(0.45f, 0.43f, 0.40f);
            RenderSettings.ambientGroundColor = new Color(0.28f, 0.24f, 0.20f);
            if (GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset urp)
            {
                urp.shadowDistance = 260f;
                urp.shadowCascadeCount = 4;
            }
        }

        void SetupCamera()
        {
            var cam = Camera.main;
            if (cam == null)
            {
                cam = new GameObject("Main Camera").AddComponent<Camera>();
                cam.tag = "MainCamera";
            }
            cam.fieldOfView = 40f;
            cam.nearClipPlane = 0.5f;
            cam.farClipPlane = 1500f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.05f, 0.06f, 0.09f);
            var rigGo = new GameObject("Camera Rig");
            rig = rigGo.AddComponent<RtsCamera>();
            rig.cam = cam;
            cam.transform.SetParent(rigGo.transform, false);
        }

        void Build(string id)
        {
            if (world != null) Destroy(world);
            world = new GameObject("World");
            map = MapBuilder.Load(id);
            mapId = id;
            MapBuilder.BuildTerrain(map).transform.SetParent(world.transform, false);
            rig.boundsMin = new Vector2(0, -map.px_h * MapBuilder.Unit);
            rig.boundsMax = new Vector2(map.px_w * MapBuilder.Unit, 0);

            foreach (var s in map.sites)
            {
                if (s.main == "player") BuildBase(s, true);
                else if (s.main == "alien") BuildBase(s, false);
                else Place("bld_site", s.x, s.y, 0);
            }
            foreach (var r in map.relays) Place("bld_relay_none", r[0], r[1], 0);
            foreach (var c in map.crates) Place("bld_crate", c[0], c[1], 0);

            if (showArmies) LineUpArmies();
            rig.JumpTo(playerBase + new Vector3(18f, 0, 6f));
        }

        void BuildBase(MapData.SiteData s, bool human)
        {
            Place(human ? "bld_apron_player" : "bld_apron_alien", s.x, s.y, 0);
            Place(human ? "bld_hq2" : "bld_hive2", s.x, s.y, 0);
            var list = human ? PlayerBuildings : AlienBuildings;
            for (int i = 0; i < s.slots.Length && i < list.Length; i++) Place("bld_" + list[i], s.slots[i][0], s.slots[i][1], 0);
            for (int i = 0; i < s.sockets.Length; i++)
            {
                float ang = Mathf.Atan2(s.sockets[i][1] - s.y, s.sockets[i][0] - s.x);   // face outward
                Place(human ? "tur_" + PlayerTurrets[i % PlayerTurrets.Length] : "bld_spine", s.sockets[i][0], s.sockets[i][1], ang);
            }
            if (human) playerBase = MapBuilder.Ground(map, s.x, s.y);
            else alienBase = MapBuilder.Ground(map, s.x, s.y);
        }

        void LineUpArmies()
        {
            var p = map.sites[0];
            var a = map.sites.Length > 1 ? map.sites[1] : p;
            // a column of each side's units heading toward the other base, starting just outside each base
            Formation(HumanArmy, p.x, p.y, a.x, a.y);
            Formation(AlienArmy, a.x, a.y, p.x, p.y);
        }

        void Formation(string[] kinds, float fx, float fy, float tx, float ty)
        {
            float ang = Mathf.Atan2(ty - fy, tx - fx);
            Vector2 dir = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)), side = new Vector2(-dir.y, dir.x);
            for (int i = 0; i < kinds.Length; i++)
            {
                int row = i / 6, col = i % 6;
                Vector2 pos = new Vector2(fx, fy) + dir * (260f + row * 70f) + side * ((col - 2.5f) * 60f);
                int members = kinds[i] == "marines" || kinds[i] == "rockets" || kinds[i] == "flamers" || kinds[i] == "skitters"
                    ? 4 : kinds[i] == "medics" || kinds[i] == "snipers" || kinds[i] == "commandos" || kinds[i] == "spitters" ? 3 : 1;
                for (int m = 0; m < members; m++)
                {
                    Vector2 off = members == 1 ? Vector2.zero : new Vector2(Mathf.Cos(m * 2.4f), Mathf.Sin(m * 2.4f)) * 12f;
                    Place(kinds[i], pos.x + off.x, pos.y + off.y, ang);
                }
            }
        }

        /// <summary>Spawns a model at a game-pixel position; angle is the map heading (radians, y-down).</summary>
        GameObject Place(string model, float px, float py, float angle)
        {
            var go = ModelLibrary.Spawn(model, world.transform);
            if (go == null) return null;
            go.transform.position = MapBuilder.Ground(map, px, py);
            go.transform.rotation = Quaternion.Euler(0, angle * Mathf.Rad2Deg, 0);
            return go;
        }

        void OnGUI()
        {
            GUILayout.BeginArea(new Rect(10, 10, 300, 400), GUI.skin.box);
            GUILayout.Label("<b>Outpost Omega - 3D look test</b>", new GUIStyle(GUI.skin.label) { richText = true });
            GUILayout.Label("Arrows / edges / middle drag: pan   Wheel: zoom\nPage Up/Down or Alt+middle drag: rotate   Home: reset");
            if (maps != null)
                foreach (var id in maps)
                    if (GUILayout.Button((id == mapId ? "> " : "") + id)) Build(id);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Your base")) rig.JumpTo(playerBase);
            if (GUILayout.Button("Alien base")) rig.JumpTo(alienBase);
            GUILayout.EndHorizontal();
            bool armies = GUILayout.Toggle(showArmies, "Show both armies");
            if (armies != showArmies) { showArmies = armies; Build(mapId); }
            GUILayout.EndArea();
        }
    }
}
