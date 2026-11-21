using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Outpost.EditorTools
{
    /// <summary>Editor menu for the Outpost Omega project ("Outpost" in the menu bar).</summary>
    [InitializeOnLoad]
    public static class OutpostMenu
    {
        const string SandboxPath = "Assets/Outpost/Scenes/Sandbox.unity";
        const string SkirmishPath = "Assets/Outpost/Scenes/Battle.unity";   // Skirmish.unity until M2 (renamed, GUID kept)

        static OutpostMenu()
        {
            // first time the project opens: create the look-test scene so there is something to press Play on
            EditorApplication.delayCall += () =>
            {
                if (!System.IO.File.Exists(SandboxPath) && !EditorApplication.isPlayingOrWillChangePlaymode)
                    CreateSandbox();
            };
        }

        [MenuItem("Outpost/Create or Reset Sandbox Scene", priority = 1)]
        public static void CreateSandbox()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            System.IO.Directory.CreateDirectory("Assets/Outpost/Scenes");
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            new GameObject("Outpost Sandbox").AddComponent<Outpost.Sandbox.SandboxBootstrap>();
            var cam = new GameObject("Main Camera");
            cam.tag = "MainCamera";
            cam.AddComponent<Camera>();
            cam.AddComponent<AudioListener>();
            cam.transform.position = new Vector3(60, 60, -120);
            cam.transform.rotation = Quaternion.Euler(50, 0, 0);
            EditorSceneManager.SaveScene(scene, SandboxPath);
            var list = EditorBuildSettings.scenes.ToList();
            if (!list.Any(s => s.path == SandboxPath))
            {
                list.Insert(0, new EditorBuildSettingsScene(SandboxPath, true));
                EditorBuildSettings.scenes = list.ToArray();
            }
            Debug.Log("[Outpost] Sandbox scene ready: " + SandboxPath + ". Press Play to fly around.");
        }

        [MenuItem("Outpost/Open Sandbox Scene", priority = 2)]
        public static void OpenSandbox()
        {
            if (!System.IO.File.Exists(SandboxPath)) { CreateSandbox(); return; }
            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) EditorSceneManager.OpenScene(SandboxPath);
        }

        [MenuItem("Outpost/Create or Reset Battle Scene", priority = 10)]
        public static void CreateSkirmishMenu() => CreateSkirmish(true);

        public static void CreateSkirmish(bool open)
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            System.IO.Directory.CreateDirectory("Assets/Outpost/Scenes");
            var previous = EditorSceneManager.GetActiveScene().path;
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            new GameObject("Battle Session").AddComponent<Outpost.Game.BattleSession>();
            var cam = new GameObject("Main Camera");
            cam.tag = "MainCamera";
            cam.AddComponent<Camera>();
            cam.AddComponent<AudioListener>();
            EditorSceneManager.SaveScene(scene, SkirmishPath);
            var list = EditorBuildSettings.scenes.ToList();
            // Boot and MainMenu stay first; the battle scene is added after them if it is missing
            if (!list.Any(s => s.path == SkirmishPath)) list.Add(new EditorBuildSettingsScene(SkirmishPath, true));
            EditorBuildSettings.scenes = list.ToArray();
            if (!open && !string.IsNullOrEmpty(previous)) EditorSceneManager.OpenScene(previous);
            Debug.Log("[Outpost] Battle scene ready: " + SkirmishPath + ". Press Play to fight a default Skirmish (or play Boot.unity for the menus).");
        }

        [MenuItem("Outpost/Open Battle Scene", priority = 11)]
        public static void OpenSkirmish()
        {
            if (!System.IO.File.Exists(SkirmishPath)) { CreateSkirmish(true); return; }
            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) EditorSceneManager.OpenScene(SkirmishPath);
        }

        [MenuItem("Outpost/Open Exported Content Folder", priority = 20)]
        public static void OpenContent()
        {
            EditorUtility.RevealInFinder(System.IO.Path.Combine(Application.streamingAssetsPath, "Outpost", "models.pak.json"));
        }

        // ------------------------------------------------------------------ combat effects test (roadmap step 5)
        // The unfocused editor barely advances play mode, so effects are checked as stills in edit mode: each case is
        // spawned through the same IBattleFx calls the Sim makes, all cases are advanced by hand to their own age, and
        // the result is photographed (manage_camera screenshot). EffectsPoolStress checks the pool stays bounded.
        const string FxTestRoot = "FX Test (temporary)";
        public static readonly Vector3 FxTestCenter = new Vector3(500f, 0f, 0f);

        class FxCase
        {
            public string name;
            public float age;
            public System.Action spawn;
            public System.Action<float> update;
        }

        [MenuItem("Outpost/Effects Test/Stills Page 1", priority = 40)] static void FxPage0() => Debug.Log(EffectsStills(0));
        [MenuItem("Outpost/Effects Test/Stills Page 2", priority = 41)] static void FxPage1() => Debug.Log(EffectsStills(1));
        [MenuItem("Outpost/Effects Test/Stills Page 3", priority = 42)] static void FxPage2() => Debug.Log(EffectsStills(2));
        [MenuItem("Outpost/Effects Test/Stills Page 4", priority = 43)] static void FxPage3() => Debug.Log(EffectsStills(3));
        [MenuItem("Outpost/Effects Test/Stills Page 5 (status marks)", priority = 44)] static void FxPage4() => Debug.Log(EffectsStills(4));
        [MenuItem("Outpost/Effects Test/Status, Float and Shake Check", priority = 47)] static void FxStatus() => Debug.Log(EffectsStatusCheck());
        [MenuItem("Outpost/Effects Test/Clear", priority = 45)] static void FxClear() => EffectsTestClear();
        [MenuItem("Outpost/Effects Test/Pool Stress Test", priority = 46)] static void FxStress() => Debug.Log(EffectsPoolStress(1800, 12));

        /// <summary>Removes the effects test objects.</summary>
        public static void EffectsTestClear()
        {
            var old = GameObject.Find(FxTestRoot);
            if (old != null) Object.DestroyImmediate(old);
        }

        static Outpost.View.Fx NewTestFx(bool ground)
        {
            EffectsTestClear();
            Outpost.Sim.GameData.Reload();
            var root = new GameObject(FxTestRoot) { hideFlags = HideFlags.DontSave };
            if (ground)
            {
                var g = GameObject.CreatePrimitive(PrimitiveType.Quad);
                Object.DestroyImmediate(g.GetComponent<Collider>());
                g.hideFlags = HideFlags.DontSave;
                g.transform.SetParent(root.transform, false);
                g.transform.position = FxTestCenter - Vector3.up * 0.02f;
                g.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
                g.transform.localScale = new Vector3(90f, 90f, 1f);
                g.GetComponent<MeshRenderer>().sharedMaterial = Outpost.View.Fx.Unlit(new Color(0.23f, 0.21f, 0.18f));
            }
            return root.AddComponent<Outpost.View.Fx>();
        }

        /// <summary>Spawns one page (9 cases, 3 x 3 cells 18 units apart around FxTestCenter) of effect stills and
        /// returns the legend. Photograph from FxTestCenter + (0, 52, -44) looking at FxTestCenter.</summary>
        public static string EffectsStills(int page)
        {
            var fx = NewTestFx(true);
            fx.Pool.viewFrom = FxTestCenter + new Vector3(0f, 52f, -44f);
            var cases = new System.Collections.Generic.List<FxCase>();
            int cell = 0;
            System.Func<Vector2> next = () =>
            {
                int k = cell++ % 9;
                float gx = (k % 3 - 1) * 180f, gy = (k / 3 - 1) * 180f;      // game pixels; top row first
                return new Vector2(FxTestCenter.x * 10f + gx, gy);
            };
            System.Func<string, string, Vector2, Outpost.Sim.Tower> tower = (kind, faction, at) =>
            {
                var tw = new Outpost.Sim.Tower(kind, faction, at.x, at.y, null, 0);
                tw.ApplyTier();
                return tw;
            };
            System.Func<string, string, Vector2, Outpost.Sim.Unit> unit = (kind, faction, at) => new Outpost.Sim.Unit(kind, faction, at.x, at.y);
            var right = new Vector2(70f, 20f);
            var p0 = next(); var p1 = next(); var p2 = next(); var p3 = next(); var p4 = next(); var p5 = next(); var p6 = next(); var p7 = next(); var p8 = next();
            switch (page)
            {
                case 0:
                    cases.Add(new FxCase { name = "marines firing (4 tracers)", age = 0.03f, spawn = () => { var u = unit("marines", "player", p0 - right); u.target = unit("skitters", "alien", p0 + right); fx.Shot(u, u.Pos, p0 + right, "player", 4); } });
                    cases.Add(new FxCase { name = "flamers (burn)", age = 0.15f, spawn = () => { var u = unit("flamers", "player", p1 - right); u.burn = true; fx.Shot(u, u.Pos, p1 + right, "player", 3); } });
                    cases.Add(new FxCase { name = "pulse tower bolt in flight", age = 0.12f, spawn = () =>
                    {
                        var tw = tower("pulse", "player", p2 - right);
                        var en = unit("brutes", "alien", p2 + right);
                        fx.TowerFired(tw, en);
                        var pr = new Outpost.Sim.Projectile { kind = "bolt", pos = tw.Pos, dest = en.Pos, target = en, src = tw, faction = "player", speed = 560 };
                        fx.ProjectileSpawned(pr);
                        cases[2].update = dt => pr.pos = Vector2.MoveTowards(pr.pos, pr.dest, 560f * dt);
                    } });
                    cases.Add(new FxCase { name = "railgun (pierce)", age = 0.05f, spawn = () => { var tw = tower("railgun", "player", p3 - right); var en = unit("brutes", "alien", p3 + right); tw.target = en; fx.TowerFired(tw, en); fx.Beam(tw.Pos, tw.Pos + (en.Pos - tw.Pos).normalized * 180f); } });
                    cases.Add(new FxCase { name = "laser, fully ramped", age = 0.05f, spawn = () => { var tw = tower("laser", "player", p4 - right); var en = unit("brutes", "alien", p4 + right); tw.target = en; tw.beamT = 5f; fx.TowerFired(tw, en); fx.Beam(tw.Pos, en.Pos); } });
                    cases.Add(new FxCase { name = "ion cannon", age = 0.08f, spawn = () => { var tw = tower("ion", "player", p5 - right); var en = unit("brutes", "alien", p5 + right); tw.target = en; fx.TowerFired(tw, en); fx.Beam(tw.Pos, en.Pos); fx.Impact(en.Pos, "player", 30, false); } });
                    cases.Add(new FxCase { name = "tesla chain (3)", age = 0.05f, spawn = () => { var tw = tower("tesla", "player", p6 - right); fx.TowerFired(tw, null); fx.Lightning(new System.Collections.Generic.List<Vector2> { tw.Pos, p6 + new Vector2(30, -30), p6 + new Vector2(80, 10), p6 + new Vector2(110, -40) }, false); } });
                    cases.Add(new FxCase { name = "lightning tower", age = 0.05f, spawn = () => { var tw = tower("lightning", "player", p7 - right); fx.TowerFired(tw, null); fx.Lightning(new System.Collections.Generic.List<Vector2> { tw.Pos, p7 + new Vector2(40, 20), p7 + new Vector2(100, -10) }, false); } });
                    cases.Add(new FxCase { name = "flamer tower (cone)", age = 0.15f, spawn = () => { var tw = tower("flamer", "player", p8 - right); fx.TowerFired(tw, null); fx.Shot(tw, tw.Pos, p8 + right, "player", 2); } });
                    break;
                case 1:
                    cases.Add(new FxCase { name = "drone tower (3 drones)", age = 0.04f, spawn = () => { var tw = tower("drone", "player", p0 - right); fx.TowerFired(tw, null); fx.Shot(tw, tw.Pos, p0 + right, "player", 3); } });
                    cases.Add(new FxCase { name = "missile burst in flight", age = 0.35f, spawn = () =>
                    {
                        var tw = tower("missile", "player", p1 - right * 1.5f);
                        var en = unit("gliders", "alien", p1 + right * 1.5f);
                        fx.TowerFired(tw, en);
                        var list = new System.Collections.Generic.List<Outpost.Sim.Projectile>();
                        for (int n = 0; n < 3; n++)
                        {
                            var pr = new Outpost.Sim.Projectile { kind = "rocket", pos = tw.Pos, dest = en.Pos, target = null, src = tw, faction = "player", speed = 380, delay = n * 0.12f };
                            fx.ProjectileSpawned(pr);
                            list.Add(pr);
                        }
                        cases[1].update = dt => { foreach (var pr in list) { if (pr.delay > 0) { pr.delay -= dt; continue; } pr.pos = Vector2.MoveTowards(pr.pos, pr.dest, 380f * dt); } };
                    } });
                    cases.Add(new FxCase { name = "mortar shell mid-arc", age = 0.5f, spawn = () =>
                    {
                        var tw = tower("mortar", "player", p2 - right * 1.5f);
                        fx.TowerFired(tw, null);
                        var pr = new Outpost.Sim.Projectile { kind = "lob", lob = true, pos = tw.Pos, dest = p2 + right * 1.5f, src = tw, faction = "player", total = 1f };
                        fx.ProjectileSpawned(pr);
                        cases[2].update = dt => pr.t += dt;
                    } });
                    cases.Add(new FxCase { name = "splash explosion (50 px), 0.12 s", age = 0.12f, spawn = () => fx.Impact(p3, "player", 50, false) });
                    cases.Add(new FxCase { name = "same explosion at 0.6 s (smoke, scorch)", age = 0.6f, spawn = () => fx.Impact(p4, "player", 50, false) });
                    cases.Add(new FxCase { name = "alien acid glob splash", age = 0.15f, spawn = () => { var pr = new Outpost.Sim.Projectile { kind = "acid", dest = p5, faction = "alien" }; fx.ProjectileEnded(pr); fx.Impact(p5, "alien", 40, false); } });
                    cases.Add(new FxCase { name = "acid tower patch, 1 s", age = 1.0f, spawn = () => { var tw = tower("acid", "player", p6 - right); fx.TowerFired(tw, null); fx.Zone(p6 + right, 44, 4f, "acid"); fx.Shot(tw, tw.Pos, p6 + right, "player", 1); } });
                    cases.Add(new FxCase { name = "napalm (flamers ability), 1 s", age = 1.0f, spawn = () => { var u = unit("flamers", "player", p7 - right * 1.5f); fx.Ability(u, "napalm", p7 + right); fx.Zone(p7 + right, 50, 5f, "fire"); fx.Trail(u.Pos, p7 + right, "player"); } });
                    cases.Add(new FxCase { name = "sonic tower shockwave", age = 0.15f, spawn = () => { var tw = tower("sonic", "player", p8); fx.TowerFired(tw, null); fx.Ring(tw.Pos, tw.range, "energy"); } });
                    break;
                case 2:
                    cases.Add(new FxCase { name = "cryo aura pulse", age = 0.45f, spawn = () => fx.TowerFired(tower("cryo", "player", p0), null) });
                    cases.Add(new FxCase { name = "gravity well pulse", age = 0.4f, spawn = () => fx.TowerFired(tower("gravity", "player", p1), null) });
                    cases.Add(new FxCase { name = "chrono stasis", age = 0.1f, spawn = () => { var tw = tower("chrono", "player", p2 - right); var en = unit("brutes", "alien", p2 + right); fx.TowerFired(tw, en); fx.Shot(tw, tw.Pos, en.Pos, "player", 1); fx.Ring(en.Pos, 22, "energy"); } });
                    cases.Add(new FxCase { name = "hacker", age = 0.1f, spawn = () => { var tw = tower("hacker", "player", p3 - right); var en = unit("brutes", "alien", p3 + right); fx.TowerFired(tw, en); fx.Shot(tw, tw.Pos, en.Pos, "player", 1); fx.Ring(en.Pos, 22, "energy"); } });
                    cases.Add(new FxCase { name = "shock fence", age = 0.05f, spawn = () => { var tw = tower("fence", "player", p4); fx.TowerFired(tw, null); for (int n = 0; n < 3; n++) fx.Shot(tw, tw.Pos, p4 + new Vector2(60 * Mathf.Cos(n * 2.1f), 60 * Mathf.Sin(n * 2.1f)), "player", 1); } });
                    cases.Add(new FxCase { name = "alien unit dies (brute goo)", age = 0.15f, spawn = () => fx.Died(unit("brutes", "alien", p5)) });
                    cases.Add(new FxCase { name = "player tank dies", age = 0.15f, spawn = () => fx.Died(unit("tank", "player", p6)) });
                    cases.Add(new FxCase { name = "player tower destroyed", age = 0.2f, spawn = () => fx.Died(tower("pulse", "player", p7)) });
                    cases.Add(new FxCase { name = "bunker firing", age = 0.04f, spawn = () => { var tw = tower("bunker", "player", p8 - right); fx.Shot(tw, tw.Pos, p8 + right, "player", 3); } });
                    break;
                case 4:
                {
                    System.Func<string, Vector2, float, float, Outpost.Sim.Unit> marked = (kind, at, stun, slow) =>
                    {
                        var u = unit(kind, "alien", at);
                        u.stunT = stun;
                        u.slow = slow;
                        u.slowT = slow > 0 ? 1f : 0f;
                        fx.TrackStatus(u);
                        return u;
                    };
                    cases.Add(new FxCase { name = "stunned brute (star overhead)", age = 0.3f, spawn = () => marked("brutes", p0, 2f, 0f) });
                    cases.Add(new FxCase { name = "slowed brute (frost ring)", age = 0.3f, spawn = () => marked("brutes", p1, 0f, 0.4f) });
                    cases.Add(new FxCase { name = "stunned and slowed wrecker", age = 0.3f, spawn = () => marked("wrecker", p2, 2f, 0.45f) });
                    cases.Add(new FxCase { name = "sonic tower stuns three", age = 0.25f, spawn = () =>
                    {
                        var tw = tower("sonic", "player", p3 - right);
                        fx.TowerFired(tw, null);
                        fx.Ring(tw.Pos, tw.range, "energy");
                        for (int n = 0; n < 3; n++) marked("skitters", p3 + new Vector2(40 * n - 20, 30 * (n % 2)), 0.7f, 0f);
                    } });
                    cases.Add(new FxCase { name = "cryo tower slows three", age = 0.5f, spawn = () =>
                    {
                        var tw = tower("cryo", "player", p4 - right);
                        fx.TowerFired(tw, null);
                        for (int n = 0; n < 3; n++) marked("skitters", p4 + new Vector2(40 * n - 20, 30 * (n % 2)), 0f, 0.4f);
                    } });
                    cases.Add(new FxCase { name = "chrono tower freezes a brute", age = 0.15f, spawn = () =>
                    {
                        var tw = tower("chrono", "player", p5 - right);
                        var en = marked("brutes", p5 + right, 2f, 0f);
                        fx.TowerFired(tw, en);
                        fx.Shot(tw, tw.Pos, en.Pos, "player", 1);
                        fx.Ring(en.Pos, 22, "energy");
                    } });
                    break;
                }
                default:
                {
                    System.Action<string, string, Vector2, Vector2> ab = (kind, id, at, aim) =>
                    {
                        var u = unit(kind, kind == "wrecker" || kind == "burrowers" ? "alien" : "player", at);
                        fx.Ability(u, id, aim);
                        float r = Outpost.Sim.GameData.F(u.ability, "radius");
                        if (id == "stomp" || id == "rally") fx.Ring(u.Pos, r, "energy");
                        if (id == "stim") fx.Ring(u.Pos, u.radius + 14, "stim");
                        if (id == "camo") fx.Ring(u.Pos, 24, "goo");
                        if (id == "burrow") fx.Ring(u.Pos, 30, "burrow");
                    };
                    cases.Add(new FxCase { name = "stomp (wrecker)", age = 0.15f, spawn = () => ab("wrecker", "stomp", p0, p0) });
                    cases.Add(new FxCase { name = "rally (commander)", age = 0.3f, spawn = () => ab("commander", "rally", p1, p1) });
                    cases.Add(new FxCase { name = "stim (marines)", age = 0.15f, spawn = () => ab("marines", "stim", p2, p2) });
                    cases.Add(new FxCase { name = "jetpack take-off (commandos)", age = 0.15f, spawn = () => ab("commandos", "jetpack", p3, p3 + right) });
                    cases.Add(new FxCase { name = "burrow (burrowers)", age = 0.2f, spawn = () => ab("burrowers", "burrow", p4 - right, p4 + right) });
                    cases.Add(new FxCase { name = "sensor array scan, 1 s", age = 1.0f, spawn = () => fx.Zone(p5, 70, 12f, "scan") });
                    cases.Add(new FxCase { name = "salvo (hornet): targets + first strike", age = 0.3f, spawn = () => { ab("hornet", "salvo", p6 - right * 1.5f, p6 + right); fx.Trail(p6 - right * 1.5f, p6 + right, "player"); fx.Impact(p6 + right, "player", 40, false); } });
                    cases.Add(new FxCase { name = "upgrade complete", age = 0.3f, spawn = () => fx.Ring(p7, 50, "upgrade") });
                    cases.Add(new FxCase { name = "power hook (step 6 placeholder)", age = 0.15f, spawn = () => fx.Abilities.Power("strike", "player", fx.Ground(p8), 6f) });
                    break;
                }
            }
            // advance: each case spawns so that it has exactly its own age at the end
            const float dt = 1f / 60f;
            float maxAge = 0f;
            foreach (var cs in cases) maxAge = Mathf.Max(maxAge, cs.age);
            var order = cases.OrderByDescending(cs => cs.age).ToList();
            float now = 0f;
            int idx = 0;
            for (int guard = 0; guard < 10000; guard++)
            {
                while (idx < order.Count && maxAge - order[idx].age <= now + 1e-4f) order[idx++].spawn();
                if (now >= maxAge - 1e-4f) break;
                foreach (var cs in cases) if (cs.update != null) cs.update(dt);
                fx.Tick(dt);
                now += dt;
            }
            fx.Tick(0f);
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"page {page + 1}: cells left to right, top row first (top row = farthest from the camera)");
            for (int n = 0; n < cases.Count; n++) sb.AppendLine($"  {n + 1}. {cases[n].name} @ {cases[n].age:0.00} s");
            sb.AppendLine($"pool: live {fx.Pool.LiveCount}, created {fx.Pool.Created}, particles {fx.Pool.Particles}, repeaters {fx.Pool.RepeaterCount}");
            return sb.ToString();
        }

        /// <summary>Checks the status marks, floating words, decal slopes and camera shake with numbers.</summary>
        public static string EffectsStatusCheck()
        {
            var sb = new System.Text.StringBuilder();
            var fx = NewTestFx(false);
            var u = new Outpost.Sim.Unit("brutes", "alien", 5000, 0);
            fx.TrackStatus(u);
            fx.Tick(0.1f);
            sb.AppendLine($"idle unit: marks {fx.StatusMarkCount}, live pieces {fx.Pool.LiveCount}");
            u.stunT = 1f; u.slow = 0.4f; u.slowT = 1f;
            fx.Tick(0.1f);
            sb.AppendLine($"stunned + slowed: marks {fx.StatusMarkCount}, live pieces {fx.Pool.LiveCount} (expect 1 mark, 3 pieces)");
            u.stunT = 0f;
            fx.Tick(0.1f);
            sb.AppendLine($"stun over: live pieces {fx.Pool.LiveCount} (expect 1)");
            u.alive = false;
            fx.Tick(0.1f);
            sb.AppendLine($"dead: marks {fx.StatusMarkCount}, tracked {fx.TrackedCount}, live pieces {fx.Pool.LiveCount} (expect 0, 0, 0)");
            for (int i = 0; i < 100; i++) fx.Float(new Vector2(5000, 0), "+16", "number");
            sb.AppendLine($"100 floats: kept {fx.FloatCount} (cap {Outpost.View.Fx.MaxFloats})");
            for (int i = 0; i < 40; i++) fx.Tick(0.05f);
            sb.AppendLine($"after 2 s: floats {fx.FloatCount} (expect 0)");
            EffectsTestClear();
            // decals on a real slope: build a mapped Fx and compare a scorch's normal with the ground
            var map = Outpost.Content.MapBuilder.Load("dune_sea");
            var mf = NewTestFx(false);
            mf.map = map;
            float best = 0f; Vector2 steep = Vector2.zero;
            for (int y = 200; y < map.px_h - 200; y += 60)
                for (int x = 200; x < map.px_w - 200; x += 60)
                {
                    float d = Mathf.Abs(Outpost.Content.MapBuilder.Ground(map, x + 30, y).y - Outpost.Content.MapBuilder.Ground(map, x - 30, y).y);
                    if (d > best) { best = d; steep = new Vector2(x, y); }
                }
            mf.Impact(steep, "player", 50, false);
            Vector3 n = Vector3.up;
            foreach (Transform c in mf.Pool.root)
                if (c.gameObject.activeSelf && c.name == "fx_quad")
                {
                    var mr = c.GetComponent<MeshRenderer>();
                    if (mr.sharedMaterial.name.StartsWith("fx_Disc_Alpha")) n = c.rotation * Vector3.back;
                }
            sb.AppendLine($"scorch on the steepest spot found ({best:0.00} units of rise over 6): tilt {Vector3.Angle(Vector3.up, n):0.0} deg from flat");
            EffectsTestClear();
            // shake needs an active RtsCamera (play mode); in edit mode it must do nothing
            float before = Outpost.View.RtsCamera.Trauma;
            Outpost.View.RtsCamera.ShakeAt(Vector3.zero, 0.5f);
            sb.AppendLine($"ShakeAt with no camera: trauma {before:0.00} -> {Outpost.View.RtsCamera.Trauma:0.00} (expect no change)");
            return sb.ToString();
        }

        /// <summary>Spawns many effects every frame for a while (explosions, tracers, beams, lightning, patches,
        /// projectiles, deaths), advancing by hand, then lets everything die out. Reports the object counts.</summary>
        public static string EffectsPoolStress(int frames, int perFrame)
        {
            var fx = NewTestFx(false);
            var root = fx.transform;
            var tw = new Outpost.Sim.Tower("laser", "player", 5000, 0, null, 0);
            var tesla = new Outpost.Sim.Tower("tesla", "player", 5000, 0, null, 0);
            var cryo = new Outpost.Sim.Tower("cryo", "player", 5000, 0, null, 0);
            tw.ApplyTier(); tesla.ApplyTier(); cryo.ApplyTier();
            var mar = new Outpost.Sim.Unit("marines", "player", 4900, 0);
            var brute = new Outpost.Sim.Unit("brutes", "alien", 5100, 0);
            var flying = new System.Collections.Generic.List<Outpost.Sim.Projectile>();
            const float dt = 1f / 30f;
            int maxLive = 0, maxObjects = 0, maxParticles = 0, maxShots = 0, maxRep = 0;
            var rnd = new System.Random(7);
            System.Func<Vector2> rp = () => new Vector2(4700 + (float)rnd.NextDouble() * 600, -300 + (float)rnd.NextDouble() * 600);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            for (int f = 0; f < frames; f++)
            {
                for (int n = 0; n < perFrame; n++)
                {
                    switch ((f * perFrame + n) % 12)
                    {
                        case 0: fx.Impact(rp(), "player", 50, false); break;
                        case 1: fx.Shot(mar, mar.Pos, rp(), "player", 4); break;
                        case 2: fx.TowerFired(tw, brute); fx.Beam(tw.Pos, rp()); break;
                        case 3: fx.TowerFired(tesla, brute); fx.Lightning(new System.Collections.Generic.List<Vector2> { tesla.Pos, rp(), rp() }, false); break;
                        case 4: fx.Zone(rp(), 40, 3f, f % 2 == 0 ? "fire" : "acid"); break;
                        case 5:
                        {
                            var pr = new Outpost.Sim.Projectile { kind = f % 3 == 0 ? "rocket" : "bolt", pos = tw.Pos, dest = rp(), src = tw, faction = "player", speed = 400 };
                            fx.ProjectileSpawned(pr);
                            flying.Add(pr);
                            break;
                        }
                        case 6: fx.Died(f % 2 == 0 ? (Outpost.Sim.Entity)brute : mar); break;
                        case 7: fx.Ring(rp(), 60, "upgrade"); break;
                        case 8: fx.TowerFired(cryo, brute); break;
                        case 9: fx.Ability(mar, "stim", mar.Pos); break;
                        case 10: fx.Zone(rp(), 60, 2f, "scan"); break;
                        default: fx.Marker(fx.Ground(rp()), Color.white, 2f); break;
                    }
                }
                for (int k = flying.Count - 1; k >= 0; k--)
                {
                    var pr = flying[k];
                    pr.pos = Vector2.MoveTowards(pr.pos, pr.dest, pr.speed * dt);
                    if ((pr.pos - pr.dest).sqrMagnitude < 1f) { fx.ProjectileEnded(pr); fx.Impact(pr.dest, "player", 10, false); flying.RemoveAt(k); }
                }
                fx.Tick(dt);
                maxLive = Mathf.Max(maxLive, fx.Pool.LiveCount);
                maxParticles = Mathf.Max(maxParticles, fx.Pool.Particles);
                maxShots = Mathf.Max(maxShots, fx.ShotsInFlight);
                maxRep = Mathf.Max(maxRep, fx.Pool.RepeaterCount);
                if (f % 30 == 0) maxObjects = Mathf.Max(maxObjects, root.GetComponentsInChildren<Transform>(true).Length);
            }
            int createdAtEnd = fx.Pool.Created;
            foreach (var pr in flying) fx.ProjectileEnded(pr);
            for (int f = 0; f < 400; f++) fx.Tick(dt);     // ~13 s with nothing new: everything should be back in the pool
            int objectsAfter = root.GetComponentsInChildren<Transform>(true).Length;
            var r = $"stress: {frames} frames x {perFrame} effects (1/30 s steps) in {sw.ElapsedMilliseconds} ms\n" +
                    $"  peak live pieces {maxLive} (cap {Outpost.View.EffectPool.MaxLive}), pieces ever created {createdAtEnd}, taken over at the cap {fx.Pool.Recycled}\n" +
                    $"  peak GameObjects under Fx {maxObjects}, peak particles {maxParticles}, peak projectiles tracked {maxShots}, peak repeaters {maxRep}\n" +
                    $"  after 13 s idle: live {fx.Pool.LiveCount}, free {fx.Pool.FreeCount}, created {fx.Pool.Created}, projectiles tracked {fx.ShotsInFlight}, repeaters {fx.Pool.RepeaterCount}, particles {fx.Pool.Particles}, GameObjects {objectsAfter}";
            EffectsTestClear();
            return r;
        }
    }
}
