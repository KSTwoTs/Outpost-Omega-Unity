// ContentImport.cs · roadmap step M2 (filled Nov 21, Milestone 2 group 4).
// One click to re-run the Python export (tools/export_unity.py) for data or models from the editor.

using System.Diagnostics;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Outpost.EditorTools
{
    /// <summary>
    /// Runs the Python game's exporter with the Python project's own .venv and refreshes the editor afterwards:
    /// menu Outpost > Content > Export Data (copies data/*.json; done in C#, needs no Python packages) and Export Models (all units and
    /// turrets, several minutes). The exporter writes straight into StreamingAssets/Outpost. Maps are not offered:
    /// export_unity.py has no maps step any more (the four maps stay as they are; generated maps come in M3).
    /// The Python folder is kept in EditorPrefs (Outpost > Content > Set Python Folder...).
    /// </summary>
    public static class ContentImport
    {
        const string PrefKey = "Outpost.PythonRepo";
        const string DefaultRepo = @"D:\Personal Projects\Outpost-Omega-Updated\Outpost Omega Updated";

        public static string PythonRepo
        {
            get => EditorPrefs.GetString(PrefKey, DefaultRepo);
            set => EditorPrefs.SetString(PrefKey, value);
        }

        public static string PythonExe => Path.Combine(PythonRepo, ".venv", "Scripts", "python.exe");
        public static string Exporter => Path.Combine(PythonRepo, "tools", "export_unity.py");
        public static string OutDir => Path.Combine(Application.dataPath, "StreamingAssets", "Outpost");

        static Process running;
        static StringBuilder log;
        static string runningWhat, runningOut;
        static double started;
        /// <summary>The last finished run: exit code (-1 = could not start) and its output.</summary>
        public static int LastExit { get; private set; }
        public static string LastLog { get; private set; } = "";
        public static bool Busy => running != null;

        [MenuItem("Outpost/Content/Export Data (JSON)")]
        static void MenuData() => ExportData();

        /// <summary>
        /// The exporter's data step done here (export_unity.py export_data: copy every data/*.json into &lt;out&gt;/data), so it
        /// works without the exporter's numpy imports. Returns the files copied (-1 = no Python data folder).
        /// </summary>
        public static int ExportData(string outDir = null)
        {
            string src = Path.Combine(PythonRepo, "data");
            if (!Directory.Exists(src))
            {
                Debug.LogError("[Outpost] No Python data folder at " + src + " (Outpost > Content > Set Python Folder...)");
                return -1;
            }
            string dst = Path.Combine(outDir ?? OutDir, "data");
            Directory.CreateDirectory(dst);
            int n = 0, changed = 0;
            foreach (var f in Directory.GetFiles(src, "*.json"))
            {
                string to = Path.Combine(dst, Path.GetFileName(f));
                if (!File.Exists(to) || File.ReadAllText(to) != File.ReadAllText(f)) changed++;
                File.Copy(f, to, true);
                n++;
            }
            LastExit = 0;
            LastLog = $"copied {n} data files to {dst} ({changed} changed)";
            Debug.Log("[Outpost] Export data: " + LastLog);
            if (outDir == null)
            {
                AssetDatabase.Refresh();
                Outpost.Sim.GameData.Reload();
            }
            return n;
        }

        [MenuItem("Outpost/Content/Export Models (slow)")]
        static void MenuModels()
        {
            if (EditorUtility.DisplayDialog("Export models", "Re-export every unit and turret model from the Python sculpts? This takes several minutes and rewrites models_anim.pak (about 34 MB).", "Export", "Cancel"))
                Run("models");
        }

        [MenuItem("Outpost/Content/Set Python Folder...")]
        static void MenuFolder()
        {
            string p = EditorUtility.OpenFolderPanel("Python Outpost Omega folder (has tools/export_unity.py)", PythonRepo, "");
            if (!string.IsNullOrEmpty(p)) PythonRepo = p.Replace('/', '\\');
        }

        [MenuItem("Outpost/Content/Export Data (JSON)", true)]
        [MenuItem("Outpost/Content/Export Models (slow)", true)]
        static bool CanRun() => !Busy;

        /// <summary>What is missing before an export can run (null = ready).</summary>
        public static string Problem()
        {
            if (!File.Exists(Exporter)) return "No exporter at " + Exporter + " (Outpost > Content > Set Python Folder...)";
            if (!File.Exists(PythonExe)) return "No Python at " + PythonExe + " (make the .venv in the Python project)";
            return null;
        }

        /// <summary>Starts the exporter in the background: what = data or models (plus optional names); outDir null = StreamingAssets/Outpost.</summary>
        public static bool Run(string what, string outDir = null)
        {
            if (Busy)
            {
                Debug.LogWarning("[Outpost] An export is already running.");
                return false;
            }
            string problem = Problem();
            if (problem != null)
            {
                LastExit = -1;
                LastLog = problem;
                Debug.LogError("[Outpost] Export: " + problem);
                return false;
            }
            runningOut = outDir ?? OutDir;
            Directory.CreateDirectory(runningOut);
            var info = new ProcessStartInfo
            {
                FileName = PythonExe,
                Arguments = $"\"{Exporter}\" \"{runningOut}\" {what}",
                WorkingDirectory = PythonRepo,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            info.EnvironmentVariables["PYTHONUNBUFFERED"] = "1";
            log = new StringBuilder();
            try
            {
                running = new Process { StartInfo = info, EnableRaisingEvents = true };
                running.OutputDataReceived += (s, e) => { if (e.Data != null) lock (log) log.AppendLine(e.Data); };
                running.ErrorDataReceived += (s, e) => { if (e.Data != null) lock (log) log.AppendLine(e.Data); };
                running.Start();
                running.BeginOutputReadLine();
                running.BeginErrorReadLine();
            }
            catch (System.Exception ex)
            {
                running = null;
                LastExit = -1;
                LastLog = ex.Message;
                Debug.LogError("[Outpost] Export could not start: " + ex.Message);
                return false;
            }
            runningWhat = what;
            started = EditorApplication.timeSinceStartup;
            Debug.Log($"[Outpost] Export {what} started -> {runningOut}");
            EditorApplication.update += Poll;
            return true;
        }

        static void Poll()
        {
            if (running == null)
            {
                EditorApplication.update -= Poll;
                return;
            }
            float t = (float)(EditorApplication.timeSinceStartup - started);
            if (runningWhat.StartsWith("models") &&
                EditorUtility.DisplayCancelableProgressBar("Exporting models", $"Python exporter running... {t:0} s", Mathf.Repeat(t / 120f, 1f)))
            {
                try { running.Kill(); } catch { }
            }
            if (!running.HasExited) return;
            running.WaitForExit();
            LastExit = running.ExitCode;
            lock (log) LastLog = log.ToString();
            running.Dispose();
            running = null;
            EditorApplication.update -= Poll;
            EditorUtility.ClearProgressBar();
            if (LastExit == 0)
            {
                Debug.Log($"[Outpost] Export {runningWhat} finished in {t:0} s.\n{LastLog}");
                if (runningOut == OutDir)
                {
                    AssetDatabase.Refresh();
                    Outpost.Sim.GameData.Reload();
                }
            }
            else
            {
                string hint = LastLog.Contains("ModuleNotFoundError")
                    ? "\nThe Python .venv is missing packages: run  .venv\\Scripts\\pip install numpy scipy scikit-image fast-simplification  in the Python folder."
                    : "";
                Debug.LogError($"[Outpost] Export {runningWhat} failed (exit {LastExit}).{hint}\n{LastLog}");
            }
        }
    }
}
