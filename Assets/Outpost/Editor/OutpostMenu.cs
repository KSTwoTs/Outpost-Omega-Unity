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
        const string SkirmishPath = "Assets/Outpost/Scenes/Skirmish.unity";

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

        [MenuItem("Outpost/Create or Reset Skirmish Scene", priority = 10)]
        public static void CreateSkirmishMenu() => CreateSkirmish(true);

        public static void CreateSkirmish(bool open)
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            System.IO.Directory.CreateDirectory("Assets/Outpost/Scenes");
            var previous = EditorSceneManager.GetActiveScene().path;
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            new GameObject("Outpost Skirmish").AddComponent<Outpost.Game.SkirmishBootstrap>();
            var cam = new GameObject("Main Camera");
            cam.tag = "MainCamera";
            cam.AddComponent<Camera>();
            cam.AddComponent<AudioListener>();
            EditorSceneManager.SaveScene(scene, SkirmishPath);
            var list = EditorBuildSettings.scenes.ToList();
            list.RemoveAll(s => s.path == SkirmishPath);
            list.Insert(0, new EditorBuildSettingsScene(SkirmishPath, true));
            EditorBuildSettings.scenes = list.ToArray();
            if (!open && !string.IsNullOrEmpty(previous)) EditorSceneManager.OpenScene(previous);
            Debug.Log("[Outpost] Skirmish scene ready: " + SkirmishPath + ". Press Play to fight.");
        }

        [MenuItem("Outpost/Open Skirmish Scene", priority = 11)]
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
    }
}
