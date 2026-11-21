// BattleView.cs · roadmap step M2 (filled Nov 21, Milestone 2 group 1).
// Owns the picture of one battle (the view half of the old SkirmishBootstrap).

using UnityEngine;
using Outpost.Content;
using Outpost.Sim;

namespace Outpost.View
{
    /// <summary>
    /// The picture of one battle: camera rig, terrain, lighting, effects receiver, fog sheet, map props, markers and
    /// the player's mouse and keyboard (CommandInput). BattleSession drives it in three steps around the Sim:
    /// BuildWorld(map) -> BattleConfig.Create(map, fx, Bind) -> Started(battle). It only reads the battle.
    /// Everything it makes sits under one World object (or this GameObject), so a new battle or leaving the scene
    /// leaves nothing behind.
    /// </summary>
    public class BattleView : MonoBehaviour
    {
        public Camera cam;
        public RtsCamera rig;
        public CommandInput input;
        public Markers markers;
        public Fx fx;
        public MapData map;
        public GameObject world;
        Battle battle;
        MapProps props;

        /// <summary>Camera rig, input and markers (once per scene).</summary>
        public void Setup()
        {
            if (input != null) return;
            SetupCamera();
            input = gameObject.AddComponent<CommandInput>();
            markers = gameObject.AddComponent<Markers>();
        }

        /// <summary>Throws the old world away and builds the terrain, lighting and effects receiver for a map.</summary>
        public Fx BuildWorld(MapData m)
        {
            Setup();
            Clear();
            map = m;
            world = new GameObject("World");
            MapBuilder.BuildTerrain(map).transform.SetParent(world.transform, false);
            Lighting.Setup(world, map, cam);
            rig.boundsMin = new Vector2(0, -map.px_h * MapBuilder.Unit);
            rig.boundsMax = new Vector2(map.px_w * MapBuilder.Unit, 0);

            fx = world.AddComponent<Fx>();
            fx.map = map;
            var unitsRoot = new GameObject("Entities").transform;
            unitsRoot.SetParent(world.transform, false);
            fx.unitsRoot = unitsRoot;
            return fx;
        }

        /// <summary>Called by BattleConfig.Create between the Battle constructor and StandardStart.</summary>
        public void Bind(Battle b)
        {
            battle = b;
            EntityView.IsHidden = e => battle != null && !battle.Seen(e, "player");
            world.AddComponent<FogView>().Bind(battle, map);
            props = world.AddComponent<MapProps>();
            props.Bind(battle, map);
        }

        /// <summary>After StandardStart: props, input, markers, and the camera over the player's first base.</summary>
        public void Started(Battle b)
        {
            battle = b;
            props?.Refresh();
            input.battle = battle;
            input.fx = fx;
            input.cam = cam;
            input.rig = rig;
            input.map = map;
            input.Select(new Entity[0]);
            markers.battle = battle;
            markers.input = input;
            markers.cam = cam;
            markers.map = map;
            Base player = null;
            foreach (var x in battle.bases) if (x.faction == "player") { player = x; break; }
            if (player != null)
            {
                var toward = new Vector2(map.px_w / 2f - player.x, map.px_h / 2f - player.y).normalized;
                var look = new Vector2(player.x, player.y) + toward * 160;
                rig.JumpTo(MapBuilder.Ground(map, look.x, look.y));
            }
        }

        static readonly Vector3[] ViewCorners = { new Vector3(0, 0), new Vector3(1, 0), new Vector3(0, 1), new Vector3(1, 1) };

        /// <summary>Tells the battle what the camera shows, in game pixels (for the Our forces are under attack alert).</summary>
        public void UpdateViewRect()
        {
            if (battle == null || cam == null) return;
            var ground = new Plane(Vector3.up, new Vector3(0, rig.transform.position.y, 0));
            float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
            bool any = false;
            foreach (var c in ViewCorners)
            {
                var ray = cam.ViewportPointToRay(c);
                float d;
                if (!ground.Raycast(ray, out d)) continue;
                var p = ray.GetPoint(d);
                float gx = p.x / MapBuilder.Unit, gy = -p.z / MapBuilder.Unit;
                x0 = Mathf.Min(x0, gx); x1 = Mathf.Max(x1, gx);
                y0 = Mathf.Min(y0, gy); y1 = Mathf.Max(y1, gy);
                any = true;
            }
            battle.view = any ? new Rect(x0, y0, x1 - x0, y1 - y0) : (Rect?)null;
        }

        /// <summary>Removes the current battle's picture (the camera rig and input stay).</summary>
        public void Clear()
        {
            if (world != null) Destroy(world);
            world = null;
            fx = null;
            props = null;
            battle = null;
            EntityView.IsHidden = null;
        }

        void OnDestroy()
        {
            Clear();
            if (rig != null) Destroy(rig.gameObject);
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
