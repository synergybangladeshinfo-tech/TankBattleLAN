using System.IO;
using UnityEditor;
using UnityEngine;

namespace TankBattle.EditorTools
{
    /// <summary>
    /// Generates every texture in the game procedurally at editor time and
    /// saves them as PNG assets (with mipmaps + repeat wrap), so the project
    /// still ships zero binary art yet gets a massive visual upgrade over
    /// flat colors: grass, sand, stone tiles, bricks, concrete, metal plate,
    /// tank camo, rubber tracks, wooden planks and leaf foliage - plus
    /// normal maps for the surfaces that benefit most from depth.
    /// All albedo textures are near-grayscale so material.color tints them
    /// (per-map palettes and per-player tank colors keep working).
    /// </summary>
    public static class TextureBuilder
    {
        public const string TexDir = "Assets/Textures";
        const int Size = 512;

        // Generated once per run, consumed by PrefabBuilder / SceneBuilder.
        public static Texture2D Grass, Sand, StoneTile, StoneTileN, Brick, BrickN;
        public static Texture2D Concrete, MetalPlate, Camo, Track, Planks, Leaf;

        public static void GenerateAll()
        {
            Grass = Create("Grass", (x, y) =>
            {
                // Vivid patchy lawn: darker clumps + bright blade streaks so the
                // texture reads clearly even from the far chase camera.
                float n = Fbm(x, y, 11f, 4);
                float patch = Mathf.PerlinNoise(x * 0.03f, y * 0.03f);          // big clumps
                float blades = Mathf.PerlinNoise(x * 0.6f + 90f, y * 0.18f) > 0.58f ? 0.22f : 0f;
                float dirt = Mathf.PerlinNoise(x * 0.02f + 300f, y * 0.02f) > 0.88f ? -0.25f : 0f;
                float v = 0.5f + n * 0.45f + blades + (patch - 0.5f) * 0.4f + dirt;
                v = Mathf.Clamp01(v);
                return new Color(v * 0.42f, v * 1.05f, v * 0.38f); // rich green
            });

            Sand = Create("Sand", (x, y) =>
            {
                float n = Fbm(x, y, 6f, 3);
                float ripple = Mathf.Sin((x + n * 60f) * 0.08f) * 0.06f;
                float v = 0.72f + n * 0.22f + ripple;
                return new Color(v, v * 0.93f, v * 0.78f);
            });

            StoneTile = Create("StoneTile", (x, y) => TileColor(x, y, out _));
            StoneTileN = CreateNormal("StoneTile_N", (x, y) =>
            {
                TileColor(x, y, out float h);
                return h;
            });

            Brick = Create("Brick", (x, y) => BrickColor(x, y, out _));
            BrickN = CreateNormal("Brick_N", (x, y) =>
            {
                BrickColor(x, y, out float h);
                return h;
            });

            Concrete = Create("Concrete", (x, y) =>
            {
                float n = Fbm(x, y, 5f, 3);
                float speck = Mathf.PerlinNoise(x * 0.9f + 40f, y * 0.9f) > 0.8f ? -0.1f : 0f;
                float crack = Mathf.PerlinNoise(x * 0.02f + 7f, y * 0.02f) > 0.985f ? -0.25f : 0f;
                float v = 0.66f + n * 0.18f + speck + crack;
                return new Color(v, v, v * 1.02f);
            });

            MetalPlate = Create("MetalPlate", (x, y) =>
            {
                // Brushed metal with rivet dots on a plate grid.
                float brush = Mathf.PerlinNoise(x * 0.9f, y * 0.05f) * 0.12f;
                int cell = Size / 4;
                int lx = x % cell, ly = y % cell;
                float edge = (lx < 3 || ly < 3) ? -0.18f : 0f;
                float rivet = RivetDot(lx, ly, cell);
                float v = 0.62f + brush + edge + rivet;
                return new Color(v, v, v * 1.05f);
            });

            Camo = Create("Camo", (x, y) =>
            {
                // Two-tone gray camo blobs; player color tints it at runtime.
                float b1 = Mathf.PerlinNoise(x * 0.013f, y * 0.013f);
                float b2 = Mathf.PerlinNoise(x * 0.035f + 300f, y * 0.035f);
                float v = b1 > 0.55f ? 0.95f : (b2 > 0.5f ? 0.72f : 0.58f);
                float wear = Fbm(x, y, 10f, 2) * 0.08f;
                v += wear;
                return new Color(v, v, v);
            });

            Track = Create("Track", (x, y) =>
            {
                // Dark rubber with horizontal tread bars.
                float tread = (y / 14) % 2 == 0 ? 0.30f : 0.16f;
                float n = Fbm(x, y, 8f, 2) * 0.08f;
                float v = tread + n;
                return new Color(v, v, v * 1.05f);
            });

            Planks = Create("Planks", (x, y) =>
            {
                int plank = x / (Size / 4);
                float grain = Mathf.PerlinNoise(x * 0.02f + plank * 13f, y * 0.35f) * 0.25f;
                float gap = (x % (Size / 4)) < 4 ? -0.28f : 0f;
                float v = 0.62f + grain + gap;
                return new Color(v, v * 0.8f, v * 0.55f);
            });

            Leaf = Create("Leaf", (x, y) =>
            {
                float n = Fbm(x, y, 12f, 3);
                float hole = Mathf.PerlinNoise(x * 0.15f + 55f, y * 0.15f) > 0.78f ? -0.2f : 0f;
                float v = 0.5f + n * 0.4f + hole;
                return new Color(v * 0.45f, v, v * 0.4f);
            });

            Debug.Log("[TankBattle] Procedural textures generated.");
        }

        // ----------------------------------------------------------- patterns

        static Color TileColor(int x, int y, out float height)
        {
            int cell = Size / 4;
            int lx = x % cell, ly = y % cell;
            bool groove = lx < 5 || ly < 5;
            float n = Fbm(x, y, 6f, 3);
            // Slight per-tile brightness variation.
            int tx = x / cell, ty = y / cell;
            float tileVar = Mathf.PerlinNoise(tx * 7.13f, ty * 3.71f) * 0.14f;
            float v = groove ? 0.42f : 0.68f + n * 0.16f + tileVar;
            height = groove ? 0.2f : 0.6f + n * 0.4f;
            return new Color(v, v, v);
        }

        static Color BrickColor(int x, int y, out float height)
        {
            int bh = Size / 8;              // brick row height
            int bw = Size / 4;              // brick width
            int row = y / bh;
            int xo = (row % 2) * (bw / 2);  // offset alternate rows
            int lx = (x + xo) % bw, ly = y % bh;
            bool mortar = lx < 6 || ly < 6;
            float n = Fbm(x, y, 8f, 2);
            int bxi = (x + xo) / bw;
            float brickVar = Mathf.PerlinNoise(bxi * 5.31f, row * 9.17f) * 0.18f;
            float v = mortar ? 0.52f : 0.66f + n * 0.14f + brickVar;
            height = mortar ? 0.15f : 0.62f + n * 0.38f;
            // Slightly warm bricks, neutral mortar (map color tints the rest).
            return mortar ? new Color(v, v, v) : new Color(v, v * 0.9f, v * 0.82f);
        }

        static float RivetDot(int lx, int ly, int cell)
        {
            int m = cell - 12;
            foreach (var rx in new[] { 10, m })
                foreach (var ry in new[] { 10, m })
                {
                    float d = Mathf.Sqrt((lx - rx) * (lx - rx) + (ly - ry) * (ly - ry));
                    if (d < 5f) return 0.18f * (1f - d / 5f);
                }
            return 0f;
        }

        static float Fbm(int x, int y, float scale, int octaves)
        {
            float v = 0f, amp = 0.5f, freq = scale / Size;
            for (int i = 0; i < octaves; i++)
            {
                v += (Mathf.PerlinNoise(x * freq + i * 37f, y * freq + i * 17f) - 0.5f) * amp;
                amp *= 0.5f;
                freq *= 2f;
            }
            return v;
        }

        // --------------------------------------------------------------- io

        static Texture2D Create(string name, System.Func<int, int, Color> pixel)
        {
            string path = $"{TexDir}/{name}.png";
            var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (existing != null) return existing;

            EnsureDir();
            var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
            var px = new Color[Size * Size];
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                    px[y * Size + x] = pixel(x, y);
            tex.SetPixels(px);
            tex.Apply();
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            return Import(path, false);
        }

        /// <summary>Build a tangent-space normal map from a height function.</summary>
        static Texture2D CreateNormal(string name, System.Func<int, int, float> heightFn)
        {
            string path = $"{TexDir}/{name}.png";
            var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (existing != null) return existing;

            EnsureDir();
            var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
            var px = new Color[Size * Size];
            const float strength = 2.2f;
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                {
                    float dx = heightFn((x + 1) % Size, y) - heightFn((x - 1 + Size) % Size, y);
                    float dy = heightFn(x, (y + 1) % Size) - heightFn(x, (y - 1 + Size) % Size);
                    var n = new Vector3(-dx * strength, -dy * strength, 1f).normalized;
                    px[y * Size + x] = new Color(n.x * 0.5f + 0.5f, n.y * 0.5f + 0.5f, n.z * 0.5f + 0.5f);
                }
            tex.SetPixels(px);
            tex.Apply();
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            return Import(path, true);
        }

        static Texture2D Import(string path, bool normal)
        {
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            var imp = (TextureImporter)AssetImporter.GetAtPath(path);
            imp.wrapMode = TextureWrapMode.Repeat;
            imp.maxTextureSize = Size;
            imp.mipmapEnabled = true;
            imp.anisoLevel = 4;
            if (normal) imp.textureType = TextureImporterType.NormalMap;
            imp.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        static void EnsureDir()
        {
            if (!AssetDatabase.IsValidFolder(TexDir))
                AssetDatabase.CreateFolder("Assets", "Textures");
        }
    }
}
