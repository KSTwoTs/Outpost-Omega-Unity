using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Outpost.Content;
using Outpost.Sim;
using Outpost.View;

namespace Outpost.Game
{
    /// <summary>
    /// Phase-2 playable slice: one base each on an exported map. You start with your Command Post,
    /// a Marine squad and the Commander; the alien AI builds up from its Hive and attacks on the
    /// difficulty timer. Destroy the Hive to win; lose your Command Post and you lose.
    /// </summary>
    public class SkirmishBootstrap : MonoBehaviour
    {
        public string mapId = "dune_sea";
        public string difficulty = "Normal";
        [Tooltip("Multiplies game speed (handy for testing).")]
        public float gameSpeed = 1f;

        Battle battle;
        GameObject world;
        RtsCamera rig;
        Camera cam;
        CommandInput input;
        Hud hud;
        Fx fx;

        public Battle Battle => battle;
        public CommandInput Input => input;

        void Start()
        {
            SetupLighting();
            SetupCamera();
            input = gameObject.AddComponent<CommandInput>();
            hud = gameObject.AddComponent<Hud>();
            var maps = MapBuilder.Available();
            if (maps.Length == 0)
            {
                Debug.LogError("[Outpost] No exported maps in StreamingAssets/Outpost/maps. Run tools/export_unity.py.");
                return;
            }
            if (System.Array.IndexOf(maps, mapId) < 0) mapId = maps[0];
            Begin();
        }

        public void Begin()
        {
            if (world != null) Destroy(world);
            GameData.Reload();
            world = new GameObject("World");
            var map = MapBuilder.Load(mapId);
            MapBuilder.BuildTerrain(map).transform.SetParent(world.transform, false);
            rig.boundsMin = new Vector2(0, -map.px_h * MapBuilder.Unit);
            rig.boundsMax = new Vector2(map.px_w * MapBuilder.Unit, 0);

            fx = world.AddComponent<Fx>();
            fx.map = map;
            var unitsRoot = new GameObject("Entities").transform;
            unitsRoot.SetParent(world.transform, false);
            fx.unitsRoot = unitsRoot;

            battle = new Battle(map) { fx = fx };
            EntityView.IsHidden = e => battle != null && battle.HiddenFrom(e, "player");
            Base player = null, alien = null;
            foreach (var s in map.sites)
            {
                if (s.main == "player" || s.main == "alien")
                {
                    bool human = s.main == "player";
                    BaseApron.Build(map, s, human, world.transform);
                    var b = battle.FoundBase(s, s.main, true);
                    if (human) player = b; else alien = b;
                }
                else Decor("bld_site", map, s.x, s.y);
            }
            foreach (var r in map.relays) Decor("bld_relay_none", map, r[0], r[1]);
            foreach (var c in map.crates) Decor("bld_crate", map, c[0], c[1]);

            var st = GameData.Rts["start"];
            if (player != null && alien != null)
            {
                battle.ai = new AlienAI(battle, difficulty);
                StartingForce(player, alien, GameData.List(st, "player_units").Append("commander"));
                StartingForce(alien, player, GameData.List(st, "alien_units"));
            }

            input.battle = battle;
            input.fx = fx;
            input.cam = cam;
            input.rig = rig;
            input.map = map;
            input.Select(new Entity[0]);
            hud.battle = battle;
            hud.input = input;
            hud.fx = fx;
            hud.cam = cam;
            hud.rig = rig;
            hud.map = map;
            hud.minimapTex = MapBuilder.LastAlbedo;
            hud.restart = Begin;
            hud.paused = false;
            if (player != null)
            {
                var toward = new Vector2(map.px_w / 2f - player.x, map.px_h / 2f - player.y).normalized;
                var look = new Vector2(player.x, player.y) + toward * 160;
                rig.JumpTo(MapBuilder.Ground(map, look.x, look.y));
            }
            fx.Message("player", "Build up and destroy the alien Hive. Select your Command Post to build.");
        }

        void StartingForce(Base home, Base enemy, IEnumerable<string> kinds)
        {
            var dir = new Vector2(enemy.x - home.x, enemy.y - home.y).normalized;
            var side = new Vector2(-dir.y, dir.x);
            int i = 0;
            foreach (var k in kinds)
            {
                if (GameData.Unit(k) == null) continue;
                var p = new Vector2(home.x, home.y) + dir * 230 + side * ((i - 1) * 60);
                var u = battle.SpawnUnit(k, home.faction, p.x, p.y);
                u.angle = Mathf.Atan2(dir.y, dir.x);
                if (home.faction == "alien") battle.ai?.OnUnit(u);
                i++;
            }
        }

        void Decor(string model, MapData map, float x, float y)
        {
            var go = ModelLibrary.Spawn(model, world.transform);
            if (go != null) go.transform.position = MapBuilder.Ground(map, x, y);
        }

        void Update()
        {
            if (battle == null || hud.paused) return;
            float dt = Mathf.Min(Time.deltaTime, 0.05f) * gameSpeed;
            // big speed-ups run in several steps so fights stay stable
            int steps = Mathf.Max(1, Mathf.CeilToInt(dt / 0.05f));
            for (int i = 0; i < steps; i++) battle.Tick(dt / steps);
        }

        // ------------------------------------------------------------------ scene
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
            cam = Camera.main;
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
            rig = new GameObject("Camera Rig").AddComponent<RtsCamera>();
            rig.cam = cam;
            rig.distance = 60f;
            cam.transform.SetParent(rig.transform, false);
        }
    }
}
