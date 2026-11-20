using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Outpost.Content;
using Outpost.Sim;
using Outpost.UI;
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
        [Tooltip("The player's leader until the Skirmish setup screen exists (M2): vanguard, engineer, tactician, ignis, rime, bastion, kestrel, ledger or hawk.")]
        public string leader = "vanguard";
        [Tooltip("Multiplies game speed (handy for testing).")]
        public float gameSpeed = 1f;

        Battle battle;
        GameObject world;
        RtsCamera rig;
        Camera cam;
        CommandInput input;
        HudRoot hud;
        Markers markers;
        Fx fx;

        public Battle Battle => battle;
        public CommandInput Input => input;

        void Start()
        {
            SetupCamera();
            input = gameObject.AddComponent<CommandInput>();
            hud = gameObject.AddComponent<HudRoot>();
            markers = gameObject.AddComponent<Markers>();
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
            Lighting.Setup(world, map, cam);
            rig.boundsMin = new Vector2(0, -map.px_h * MapBuilder.Unit);
            rig.boundsMax = new Vector2(map.px_w * MapBuilder.Unit, 0);

            fx = world.AddComponent<Fx>();
            fx.map = map;
            var unitsRoot = new GameObject("Entities").transform;
            unitsRoot.SetParent(world.transform, false);
            fx.unitsRoot = unitsRoot;

            battle = new Battle(map) { fx = fx };
            battle.SetLeader(leader);
            EntityView.IsHidden = e => battle != null && !battle.Seen(e, "player");
            world.AddComponent<FogView>().Bind(battle, map);
            var props = world.AddComponent<MapProps>();
            props.Bind(battle, map);
            battle.StandardStart(difficulty);
            props.Refresh();
            var player = battle.bases.FirstOrDefault(x => x.faction == "player");

            input.battle = battle;
            input.fx = fx;
            input.cam = cam;
            input.rig = rig;
            input.map = map;
            input.Select(new Entity[0]);
            hud.Bind(battle, input, fx, rig, map, MapBuilder.LastAlbedo, Begin);
            markers.battle = battle;
            markers.input = input;
            markers.cam = cam;
            markers.map = map;
            if (player != null)
            {
                var toward = new Vector2(map.px_w / 2f - player.x, map.px_h / 2f - player.y).normalized;
                var look = new Vector2(player.x, player.y) + toward * 160;
                rig.JumpTo(MapBuilder.Ground(map, look.x, look.y));
            }
            fx.Message("player", "Build up and destroy the alien Hive. Select your Command Post to build; claim the yellow pads for more bases.");
        }

        void Update()
        {
            if (battle == null || hud.paused) return;
            float dt = Mathf.Min(Time.deltaTime, 0.05f) * gameSpeed;
            // big speed-ups run in several steps so fights stay stable
            int steps = Mathf.Max(1, Mathf.CeilToInt(dt / 0.05f));
            UpdateViewRect();
            for (int i = 0; i < steps; i++) battle.Tick(dt / steps);
        }

        static readonly Vector3[] ViewCorners = { new Vector3(0, 0), new Vector3(1, 0), new Vector3(0, 1), new Vector3(1, 1) };

        /// <summary>Tells the battle what the camera shows, in game pixels (for the Our forces are under attack alert).</summary>
        void UpdateViewRect()
        {
            var ground = new Plane(Vector3.up, new Vector3(0, rig.transform.position.y, 0));
            float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
            bool any = false;
            foreach (var c in ViewCorners)
            {
                var ray = cam.ViewportPointToRay(c);
                if (!ground.Raycast(ray, out float d)) continue;
                var p = ray.GetPoint(d);
                float gx = p.x / MapBuilder.Unit, gy = -p.z / MapBuilder.Unit;
                x0 = Mathf.Min(x0, gx); x1 = Mathf.Max(x1, gx);
                y0 = Mathf.Min(y0, gy); y1 = Mathf.Max(y1, gy);
                any = true;
            }
            battle.view = any ? new Rect(x0, y0, x1 - x0, y1 - y0) : (Rect?)null;
        }

        // ------------------------------------------------------------------ scene
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
