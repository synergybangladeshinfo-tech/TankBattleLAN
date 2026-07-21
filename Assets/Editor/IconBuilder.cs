using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace TankBattle.EditorTools
{
    /// <summary>
    /// Draws the app icon entirely in code (no art assets needed) and assigns
    /// it as the default application icon: a stylised orange tank on a round
    /// steel-blue badge over a night-sky gradient. 512x512 - Android scales
    /// every density from it.
    /// </summary>
    public static class IconBuilder
    {
        const int S = 512;
        const string IconDir = "Assets/Icons";
        const string IconPath = IconDir + "/AppIcon.png";

        // Palette.
        static readonly Color32 BgTop = new Color32(16, 24, 40, 255);
        static readonly Color32 BgBottom = new Color32(36, 58, 92, 255);
        static readonly Color32 Badge = new Color32(30, 48, 74, 255);
        static readonly Color32 Ring = new Color32(74, 163, 255, 255);
        static readonly Color32 RingInner = new Color32(140, 200, 255, 255);
        static readonly Color32 TankMain = new Color32(255, 159, 46, 255);
        static readonly Color32 TankDark = new Color32(196, 110, 18, 255);
        static readonly Color32 TrackCol = new Color32(24, 32, 47, 255);
        static readonly Color32 WheelCol = new Color32(59, 76, 99, 255);
        static readonly Color32 Muzzle = new Color32(255, 210, 120, 255);

        public static void BuildAndAssignIcon()
        {
            var px = new Color32[S * S];

            // --- background: vertical gradient + soft radial glow ---
            for (int y = 0; y < S; y++)
            {
                float ty = y / (float)(S - 1);
                Color32 row = Color32.Lerp(BgBottom, BgTop, ty);
                for (int x = 0; x < S; x++) px[y * S + x] = row;
            }
            Glow(px, S / 2, S / 2, 250, new Color32(80, 130, 190, 255), 0.35f);

            // --- round badge with a double ring ---
            FillCircle(px, 256, 256, 232, Ring);
            FillCircle(px, 256, 256, 218, RingInner);
            FillCircle(px, 256, 256, 210, Badge);

            // --- ground shadow ---
            FillEllipse(px, 256, 150, 165, 26, new Color32(10, 16, 26, 255));

            // --- tank (facing right), drawn dark outline first, then fill ---
            // tracks
            RectOutlined(px, 118, 148, 394, 210, TrackCol, TrackCol, 0);
            for (int i = 0; i < 5; i++)                       // road wheels
                FillCircle(px, 150 + i * 53, 178, 19, WheelCol);
            // hull
            RectOutlined(px, 108, 208, 404, 272, TankMain, TankDark, 6);
            // hull front slope hint (darker wedge)
            RectOutlined(px, 372, 208, 404, 272, TankDark, TankDark, 0);
            // turret
            RectOutlined(px, 186, 268, 330, 340, TankMain, TankDark, 6);
            // barrel
            RectOutlined(px, 326, 288, 470, 316, TankMain, TankDark, 5);
            // muzzle brake
            RectOutlined(px, 452, 280, 484, 324, Muzzle, TankDark, 4);
            // hatch
            FillCircle(px, 238, 340, 20, TankDark);

            // --- spark highlight on the badge (top-left) ---
            Glow(px, 160, 380, 90, new Color32(255, 255, 255, 255), 0.10f);

            // --- write the PNG + import ---
            var tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
            tex.SetPixels32(px);
            tex.Apply();

            if (!AssetDatabase.IsValidFolder(IconDir))
                AssetDatabase.CreateFolder("Assets", "Icons");
            File.WriteAllBytes(IconPath, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(IconPath, ImportAssetOptions.ForceUpdate);

            var importer = (TextureImporter)AssetImporter.GetAtPath(IconPath);
            importer.textureType = TextureImporterType.Default;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.maxTextureSize = 512;
            importer.SaveAndReimport();

            var icon = AssetDatabase.LoadAssetAtPath<Texture2D>(IconPath);
            PlayerSettings.SetIcons(NamedBuildTarget.Unknown, new[] { icon }, IconKind.Any);
            Debug.Log("[TankBattle] App icon generated and assigned.");
        }

        // ------------------------------------------------------------- drawing

        static void Put(Color32[] px, int x, int y, Color32 c)
        {
            if (x < 0 || x >= S || y < 0 || y >= S) return;
            px[y * S + x] = c;
        }

        static void FillRect(Color32[] px, int x0, int y0, int x1, int y1, Color32 c)
        {
            for (int y = y0; y < y1; y++)
                for (int x = x0; x < x1; x++)
                    Put(px, x, y, c);
        }

        /// <summary>Rect with an outline ring drawn 'border' px wider first.</summary>
        static void RectOutlined(Color32[] px, int x0, int y0, int x1, int y1,
                                 Color32 fill, Color32 outline, int border)
        {
            if (border > 0)
                FillRect(px, x0 - border, y0 - border, x1 + border, y1 + border, outline);
            FillRect(px, x0, y0, x1, y1, fill);
        }

        static void FillCircle(Color32[] px, int cx, int cy, int r, Color32 c)
        {
            int r2 = r * r;
            for (int y = -r; y <= r; y++)
                for (int x = -r; x <= r; x++)
                    if (x * x + y * y <= r2)
                        Put(px, cx + x, cy + y, c);
        }

        static void FillEllipse(Color32[] px, int cx, int cy, int rx, int ry, Color32 c)
        {
            for (int y = -ry; y <= ry; y++)
                for (int x = -rx; x <= rx; x++)
                {
                    float nx = x / (float)rx, ny = y / (float)ry;
                    if (nx * nx + ny * ny <= 1f)
                        Put(px, cx + x, cy + y, c);
                }
        }

        /// <summary>Alpha-blend a soft radial glow into the buffer.</summary>
        static void Glow(Color32[] px, int cx, int cy, int r, Color32 c, float strength)
        {
            int r2 = r * r;
            for (int y = -r; y <= r; y++)
                for (int x = -r; x <= r; x++)
                {
                    int d2 = x * x + y * y;
                    if (d2 > r2) continue;
                    int ix = cx + x, iy = cy + y;
                    if (ix < 0 || ix >= S || iy < 0 || iy >= S) continue;

                    float a = (1f - Mathf.Sqrt(d2) / r) * strength;
                    var basePx = px[iy * S + ix];
                    px[iy * S + ix] = Color32.Lerp(basePx, c, a);
                }
        }
    }
}
