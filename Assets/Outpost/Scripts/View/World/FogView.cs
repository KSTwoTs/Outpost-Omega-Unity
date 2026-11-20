// FogView.cs · roadmap step M1.7 · vision.
// Draws the player's fog of war over the terrain (game/rts/render.py draw_fog).
using UnityEngine;
using UnityEngine.Rendering;
using Outpost.Content;
using Outpost.Sim;

namespace Outpost.View
{
    /// <summary>
    /// Draws one side's fog of war (render.py draw_fog): a sheet that follows the ground a little above it, coloured
    /// FOG_RGB (6, 8, 18), fully dark over unexplored tiles, alpha 150 over explored tiles out of sight and clear where
    /// the side sees now. The fog texture has one pixel per map tile; bilinear filtering softens the tile edges like
    /// Python's smoothscale. Enemy units outside vision and enemy buildings never scouted are hidden through
    /// EntityView.IsHidden (Battle.Seen). Reads the sim; never changes it.
    /// </summary>
    public class FogView : MonoBehaviour
    {
        public static readonly Color32 FogRgb = new Color32(6, 8, 18, 255);
        public const byte UnexploredAlpha = 255, ExploredAlpha = 150;

        public Battle battle;
        public string faction = "player";
        /// <summary>World units the sheet floats above the highest ground near each of its points.</summary>
        public float lift = 0.35f;
        public Texture2D texture;
        Color32[] px;
        int shownVersion = -1;
        MeshRenderer sheet;
        Mesh mesh;
        Material mat;

        public void Bind(Battle b, MapData map)
        {
            battle = b;
            var g = b.FogOf(faction);
            if (g == null) return;
            texture = new Texture2D(g.W, g.H, TextureFormat.RGBA32, false)
            {
                name = "fog_" + faction, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear,
            };
            px = new Color32[g.W * g.H];
            mesh = BuildSheet(map, g, lift);
            var go = new GameObject("Fog");
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            sheet = go.AddComponent<MeshRenderer>();
            var baseMat = Resources.Load<Material>("Effects/fx_alpha");
            mat = baseMat != null ? new Material(baseMat) : new Material(Shader.Find("Outpost/FxAlpha"));
            mat.name = "fog";
            mat.SetTexture("_MainTex", texture);
            mat.SetColor("_Color", Color.white);
            mat.renderQueue = 2990;        // before the effects, so shots and blasts draw over it
            sheet.sharedMaterial = mat;
            sheet.shadowCastingMode = ShadowCastingMode.Off;
            sheet.receiveShadows = false;
            Refresh(true);
        }

        /// <summary>A grid over the map every 16 game pixels; each point sits lift above the highest ground within
        /// 16 px so slopes between points never poke through. UVs match the terrain's (u = x / px_w, v = 1 - y / px_h).</summary>
        static Mesh BuildSheet(MapData m, FogGrid g, float lift)
        {
            const float step = 16f;
            int W = Mathf.CeilToInt(m.px_w / step) + 1, H = Mathf.CeilToInt(m.px_h / step) + 1;
            var verts = new Vector3[W * H];
            var uvs = new Vector2[W * H];
            var cols = new Color32[W * H];
            for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                float gx = Mathf.Min(x * step, m.px_w), gy = Mathf.Min(y * step, m.px_h);
                float top = float.MinValue;
                for (int oy = -2; oy <= 2; oy++)
                for (int ox = -2; ox <= 2; ox++)
                    top = Mathf.Max(top, MapBuilder.Ground(m, gx + ox * 8f, gy + oy * 8f).y);
                int i = y * W + x;
                verts[i] = new Vector3(gx * MapBuilder.Unit, top + lift, -gy * MapBuilder.Unit);
                uvs[i] = new Vector2(gx / (g.W * g.T), 1f - gy / (g.H * g.T));
                cols[i] = new Color32(255, 255, 255, 255);
            }
            var tris = new int[(W - 1) * (H - 1) * 6];
            int t = 0;
            for (int y = 0; y < H - 1; y++)
            for (int x = 0; x < W - 1; x++)
            {
                int i = y * W + x;
                tris[t++] = i; tris[t++] = i + 1; tris[t++] = i + W;
                tris[t++] = i + 1; tris[t++] = i + W + 1; tris[t++] = i + W;
            }
            var mesh = new Mesh { name = "fog_sheet", indexFormat = IndexFormat.UInt32 };
            mesh.vertices = verts;
            mesh.uv = uvs;
            mesh.colors32 = cols;
            mesh.triangles = tris;
            mesh.RecalculateBounds();
            return mesh;
        }

        void LateUpdate()
        {
            if (battle != null) Refresh(false);
        }

        void Refresh(bool force)
        {
            var g = battle.FogOf(faction);
            if (g == null || texture == null) return;
            if (!force && g.version == shownVersion) return;
            shownVersion = g.version;
            sheet.enabled = !g.revealAll;
            if (g.revealAll) return;
            Paint(g, px, UnexploredAlpha, ExploredAlpha);
            texture.SetPixels32(px);
            texture.Apply(false);
        }

        /// <summary>Fills a tile-sized fog picture (row 0 = the bottom of the map, as textures are drawn).</summary>
        public static void Paint(FogGrid g, Color32[] px, byte unexplored, byte explored)
        {
            var c = FogRgb;
            for (int ty = 0; ty < g.H; ty++)
            {
                int row = ty * g.W, outRow = (g.H - 1 - ty) * g.W;
                for (int tx = 0; tx < g.W; tx++)
                {
                    byte s = g.State(row + tx);
                    c.a = s == 0 ? unexplored : s == 1 ? explored : (byte)0;
                    px[outRow + tx] = c;
                }
            }
        }

        void OnDestroy()
        {
            if (texture != null) Destroy(texture);
            if (mat != null) Destroy(mat);
            if (mesh != null) Destroy(mesh);
        }
    }
}
