// Lighting.cs · roadmap step M1.8 · lighting and atmosphere.
// Sun, shadows, ambient light, distance haze, glowing hazard pools and a post-processing volume (tonemapping,
// colour grade, bloom, vignette), all chosen from the map's theme so each map keeps the mood of the Python game
// (systems/level_system.py THEMES, rts/terrain_art.py STYLE and its upper-left sun). View only: reads the map, never the sim.
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Outpost.Content;

namespace Outpost.View
{
    /// <summary>One map theme's lighting. Colours are sRGB like the Python palettes; tweak them here.</summary>
    public class Mood
    {
        public string theme = "desert";
        // sun: travels toward +x / -z (from the upper left of the map, as terrain_art.py LIGHT)
        public Color sun = new Color(1f, 0.93f, 0.80f);
        public float sunIntensity = 1.65f;
        public float sunPitch = 50f, sunYaw = 140f;
        public float shadowStrength = 0.72f;
        // trilight ambient
        public Color sky = new Color(0.58f, 0.66f, 0.80f);
        public Color equator = new Color(0.55f, 0.50f, 0.44f);
        public Color ground = new Color(0.40f, 0.33f, 0.25f);
        // distance haze (exponential squared, density = hazeAmount / camera height) and the off-map background
        public Color haze = new Color(0.80f, 0.70f, 0.55f);
        public float hazeAmount = 0.22f;
        public float backgroundDim = 0.45f;
        // colour grade
        public float exposure = 0.1f, contrast = 14f, saturation = 14f;
        public Color filter = new Color(1f, 0.98f, 0.95f);
        public Vector4 shadowsTint = new Vector4(0.96f, 0.98f, 1.05f, 0f);
        public Vector4 highlightsTint = new Vector4(1.02f, 1f, 0.97f, 0f);
        // bloom: threshold just above the brightest sunlit ground so only emissive parts (2.2 in the pak) and effects glow
        public float bloomThreshold = 0.9f, bloomIntensity = 1.2f, bloomScatter = 0.65f;
        public Color bloomTint = Color.white;
        public float vignette = 0.18f;
        // hazard pools in the painted ground that glow (0 = off, 1 = full)
        public float lavaGlow = 0f, acidGlow = 0f;
        /// <summary>Emission multiplier of the glowing pools (HDR, so they pass the bloom threshold).</summary>
        public float glowStrength = 1.8f;
    }

    /// <summary>
    /// Lights a battle: made by the Game layer right after the terrain is built (Lighting.Setup), lives on the World object
    /// and goes with it. Creates the sun, ambient, haze, a global post Volume in code, turns on post-processing on the camera,
    /// and paints an emission map over lava (magma) and acid (hive) pools from the ground albedo.
    /// </summary>
    public class Lighting : MonoBehaviour
    {
        public static Lighting Active { get; private set; }

        public Mood mood;
        public Light sun;
        public Volume volume;
        public Camera cam;
        [Tooltip("Distance haze on/off.")] public bool haze = true;
        /// <summary>Milliseconds the last Setup took (mostly the glow map).</summary>
        public float setupMs;
        VolumeProfile profile;
        Texture2D glowMap;

        public const float ShadowDistance = 240f;
        public const int ShadowCascades = 4;

        public static Lighting Setup(GameObject world, MapData map, Camera camera)
        {
            var l = world.AddComponent<Lighting>();
            l.Apply(MoodFor(map != null ? map.theme : null), camera);
            return l;
        }

        public static Mood MoodFor(string theme)
        {
            switch (theme)
            {
                case "frozen":
                    return new Mood
                    {
                        theme = theme, sun = new Color(0.97f, 0.97f, 1f), sunIntensity = 1.35f, sunPitch = 46f, shadowStrength = 0.65f,
                        sky = new Color(0.60f, 0.66f, 0.78f), equator = new Color(0.55f, 0.57f, 0.62f), ground = new Color(0.45f, 0.47f, 0.52f),
                        haze = new Color(0.74f, 0.78f, 0.84f), hazeAmount = 0.22f,
                        exposure = -0.1f, contrast = 14f, saturation = 4f, filter = new Color(0.98f, 0.99f, 1f),
                        shadowsTint = new Vector4(0.97f, 0.98f, 1.04f, 0f), highlightsTint = new Vector4(1f, 1f, 1.02f, 0f),
                        bloomThreshold = 1.2f, bloomIntensity = 1f, bloomTint = new Color(0.9f, 0.96f, 1f),
                    };
                case "hive":
                    return new Mood
                    {
                        theme = theme, sun = new Color(0.95f, 0.90f, 0.95f), sunIntensity = 1.3f, sunPitch = 55f, shadowStrength = 0.7f,
                        sky = new Color(0.52f, 0.50f, 0.60f), equator = new Color(0.42f, 0.40f, 0.42f), ground = new Color(0.30f, 0.34f, 0.26f),
                        haze = new Color(0.30f, 0.27f, 0.32f), hazeAmount = 0.2f,
                        exposure = 0.15f, contrast = 14f, saturation = 10f, filter = new Color(1f, 0.98f, 1f),
                        shadowsTint = new Vector4(0.98f, 0.95f, 1.06f, 0f), highlightsTint = new Vector4(1f, 1.02f, 0.98f, 0f),
                        bloomThreshold = 0.85f, bloomIntensity = 1.4f, bloomTint = new Color(0.9f, 1f, 0.9f),
                        acidGlow = 1f, glowStrength = 3f,
                    };
                case "magma":
                    return new Mood
                    {
                        theme = theme, sun = new Color(1f, 0.86f, 0.74f), sunIntensity = 1.45f, sunPitch = 48f, shadowStrength = 0.75f,
                        sky = new Color(0.48f, 0.45f, 0.48f), equator = new Color(0.42f, 0.36f, 0.33f), ground = new Color(0.42f, 0.24f, 0.14f),
                        haze = new Color(0.28f, 0.21f, 0.18f), hazeAmount = 0.2f,
                        exposure = 0.1f, contrast = 14f, saturation = 6f, filter = new Color(1f, 0.98f, 0.96f),
                        shadowsTint = new Vector4(1.04f, 0.97f, 0.96f, 0f), highlightsTint = new Vector4(1.03f, 0.99f, 0.95f, 0f),
                        bloomThreshold = 0.85f, bloomIntensity = 1.4f, bloomTint = new Color(1f, 0.85f, 0.7f),
                        lavaGlow = 1f,
                    };
                default:
                    return new Mood { theme = string.IsNullOrEmpty(theme) ? "desert" : theme };
            }
        }

        public void Apply(Mood m, Camera camera)
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            Active = this;
            mood = m;
            cam = camera;
            BuildSun(m);
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = m.sky;
            RenderSettings.ambientEquatorColor = m.equator;
            RenderSettings.ambientGroundColor = m.ground;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = m.haze;
            if (cam != null)
            {
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = Color.Lerp(Color.black, m.haze, m.backgroundDim);
                var data = cam.GetUniversalAdditionalCameraData();
                data.renderPostProcessing = true;
                data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
                data.antialiasingQuality = AntialiasingQuality.Medium;
                data.renderShadows = true;
            }
            if (GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset urp)
            {
                // the asset is shared: only touch it when the values differ, so it is not dirtied every match
                if (Mathf.Abs(urp.shadowDistance - ShadowDistance) > 0.5f) urp.shadowDistance = ShadowDistance;
                if (urp.shadowCascadeCount != ShadowCascades) urp.shadowCascadeCount = ShadowCascades;
            }
            BuildVolume(m);
            TerrainGlow(m);
            UpdateHaze();
            setupMs = (float)watch.Elapsed.TotalMilliseconds;
        }

        void BuildSun(Mood m)
        {
            var go = new GameObject("Sun");
            go.transform.SetParent(transform, false);
            go.transform.rotation = Quaternion.Euler(m.sunPitch, m.sunYaw, 0f);
            sun = go.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = m.sun;
            sun.intensity = m.sunIntensity;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = m.shadowStrength;
            RenderSettings.sun = sun;
            // turn off any other sun (a scene light, or the last match's World while it is being destroyed)
            foreach (var other in FindObjectsByType<Light>(FindObjectsSortMode.None))
                if (other != sun && other.type == LightType.Directional) other.enabled = false;
        }

        void BuildVolume(Mood m)
        {
            var go = new GameObject("Post Volume");
            go.transform.SetParent(transform, false);
            volume = go.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 10f;
            profile = ScriptableObject.CreateInstance<VolumeProfile>();
            profile.name = "mood_" + m.theme;

            var tone = profile.Add<Tonemapping>(true);
            tone.mode.value = TonemappingMode.Neutral;

            var grade = profile.Add<ColorAdjustments>(true);
            grade.postExposure.value = m.exposure;
            grade.contrast.value = m.contrast;
            grade.saturation.value = m.saturation;
            grade.colorFilter.value = m.filter;
            grade.hueShift.value = 0f;

            var smh = profile.Add<ShadowsMidtonesHighlights>(true);
            smh.shadows.value = m.shadowsTint;
            smh.midtones.value = new Vector4(1f, 1f, 1f, 0f);
            smh.highlights.value = m.highlightsTint;

            var bloom = profile.Add<Bloom>(true);
            bloom.threshold.value = m.bloomThreshold;
            bloom.intensity.value = m.bloomIntensity;
            bloom.scatter.value = m.bloomScatter;
            bloom.tint.value = m.bloomTint;
            bloom.highQualityFiltering.value = false;
            bloom.dirtIntensity.value = 0f;

            var vig = profile.Add<Vignette>(true);
            vig.intensity.value = m.vignette;
            vig.smoothness.value = 0.45f;
            vig.rounded.value = false;
            vig.color.value = Color.black;

            volume.sharedProfile = profile;
        }

        static float Smooth(float a, float b, float v) => Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((v - a) / (b - a)));

        /// <summary>Lava (orange, bright) and acid (green) pools in the ground albedo get an emission map at half size.</summary>
        void TerrainGlow(Mood m)
        {
            if (m.lavaGlow <= 0f && m.acidGlow <= 0f) return;
            var terrain = transform.Find("Terrain");
            var mr = terrain != null ? terrain.GetComponent<MeshRenderer>() : null;
            var src = MapBuilder.LastAlbedo;
            if (mr == null || src == null || !src.isReadable) return;
            int sw = src.width, w = sw / 2, h = src.height / 2;
            var px = src.GetPixels32();
            var glow = new Color32[w * h];
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                var c = px[(y * 2) * sw + x * 2];
                float r = c.r / 255f, g = c.g / 255f, b = c.b / 255f;
                float k = 0f;
                if (m.lavaGlow > 0f) k += m.lavaGlow * Smooth(0.5f, 0.8f, r) * Smooth(0.15f, 0.35f, r - g) * (1f - Smooth(0.3f, 0.5f, b));
                if (m.acidGlow > 0f) k += m.acidGlow * Smooth(0.3f, 0.55f, g) * Smooth(0.05f, 0.2f, g - r) * Smooth(0.05f, 0.2f, g - b);
                k = Mathf.Clamp01(k);
                glow[y * w + x] = new Color32((byte)(c.r * k), (byte)(c.g * k), (byte)(c.b * k), 255);
            }
            glowMap = new Texture2D(w, h, TextureFormat.RGB24, true) { name = "glow_" + m.theme, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear };
            glowMap.SetPixels32(glow);
            glowMap.Apply(true, true);
            var mat = mr.sharedMaterial;
            mat.EnableKeyword("_EMISSION");
            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            mat.SetTexture("_EmissionMap", glowMap);
            mat.SetColor("_EmissionColor", Color.white * m.glowStrength);
        }

        void LateUpdate() => UpdateHaze();

        void UpdateHaze()
        {
            RenderSettings.fog = haze && mood != null && mood.hazeAmount > 0f;
            if (cam == null || mood == null) return;
            float height = Mathf.Max(8f, cam.transform.position.y);
            RenderSettings.fogDensity = mood.hazeAmount / height;
        }

        void OnDestroy()
        {
            if (Active == this) Active = null;
            if (profile != null) Destroy(profile);
            if (glowMap != null) Destroy(glowMap);
        }
    }
}
