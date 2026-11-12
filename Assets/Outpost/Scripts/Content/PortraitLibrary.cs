using System.Collections.Generic;
using UnityEngine;

namespace Outpost.Content
{
    /// <summary>
    /// Portraits for the HUD: each model is photographed once with a hidden camera, far below the
    /// map, and the picture is kept. Ask with Get(model); it returns null until the picture is ready
    /// (one is taken per frame), so callers just ask again later.
    /// </summary>
    public static class PortraitLibrary
    {
        const int Size = 160;
        static readonly Dictionary<string, Texture2D> done = new Dictionary<string, Texture2D>();
        static readonly HashSet<string> missing = new HashSet<string>();
        static readonly Queue<string> waiting = new Queue<string>();
        static readonly HashSet<string> queued = new HashSet<string>();
        static Camera cam;
        static RenderTexture rt;

        public static Texture2D Get(string model)
        {
            if (string.IsNullOrEmpty(model)) return null;
            if (done.TryGetValue(model, out var t) && t != null) return t;
            if (missing.Contains(model)) return null;
            if (queued.Add(model)) waiting.Enqueue(model);
            return null;
        }

        /// <summary>True when Get will never have a picture for this model (no such model).</summary>
        public static bool Missing(string model) => missing.Contains(model);

        /// <summary>Call once per frame: takes the next waiting portrait.</summary>
        public static void Tick()
        {
            if (waiting.Count == 0) return;
            string model = waiting.Dequeue();
            queued.Remove(model);
            try
            {
                var tex = Render(model);
                if (tex != null) done[model] = tex;
                else missing.Add(model);
            }
            catch (System.Exception e)
            {
                missing.Add(model);
                Debug.LogWarning($"[Outpost] Portrait for '{model}' failed: {e.Message}");
            }
        }

        static void Ensure()
        {
            if (cam != null) return;
            var go = new GameObject("Portrait Camera") { hideFlags = HideFlags.HideAndDontSave };
            cam = go.AddComponent<Camera>();
            cam.enabled = false;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.07f, 0.10f, 0.15f, 1f);
            cam.fieldOfView = 24f;
            cam.allowHDR = false;
            rt = new RenderTexture(Size, Size, 24) { hideFlags = HideFlags.HideAndDontSave };
            cam.targetTexture = rt;
        }

        static Texture2D Render(string model)
        {
            Ensure();
            var root = new GameObject("portrait " + model);
            root.transform.position = new Vector3(0f, -4000f, 0f);
            Texture2D tex = null;
            try
            {
                var m = ModelLibrary.Spawn(model, root.transform);
                if (m == null) return null;
                var renderers = root.GetComponentsInChildren<Renderer>();
                if (renderers.Length == 0) return null;
                var bounds = renderers[0].bounds;
                foreach (var r in renderers) bounds.Encapsulate(r.bounds);
                float radius = Mathf.Max(0.2f, bounds.extents.magnitude);
                // three-quarter view from the front right, a little above
                var toCam = new Vector3(0.75f, 0.62f, -0.62f).normalized;
                float dist = radius / Mathf.Sin(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * 0.8f;
                cam.transform.position = bounds.center + toCam * dist;
                cam.transform.LookAt(bounds.center);
                cam.nearClipPlane = Mathf.Max(0.05f, dist - radius * 2f);
                cam.farClipPlane = dist + radius * 2f;
                cam.Render();
                var prev = RenderTexture.active;
                RenderTexture.active = rt;
                tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false) { name = "portrait_" + model, hideFlags = HideFlags.HideAndDontSave };
                tex.ReadPixels(new Rect(0, 0, Size, Size), 0, 0);
                tex.Apply();
                RenderTexture.active = prev;
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
            return tex;
        }
    }
}
