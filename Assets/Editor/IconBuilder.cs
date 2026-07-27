using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace TankBattle.EditorTools
{
    /// <summary>
    /// Draws the app icon entirely in code (no art assets needed) and assigns it
    /// as the default application icon.
    ///
    /// v2.7 redesign: a heavy angular tank silhouette punching forward out of a
    /// hot orange muzzle flash, sitting on a dark hex-plated badge with a crisp
    /// double ring and corner "targeting" ticks. Everything is anti-aliased and
    /// the shapes are much bolder than the old icon, so it still reads clearly
    /// at 48x48 on a phone home screen. 512x512 - Android scales every density
    /// from it.
    /// </summary>
    public static class IconBuilder
    {
        const int S = 512;
        const string IconDir = "Assets/Icons";
        const string IconPath = IconDir + "/AppIcon.png";

        // ---- palette ----
        static readonly Color BgOuter = new Color32(9, 13, 22, 255);
        static readonly Color BgInner = new Color32(24, 38, 62, 255);
        static readonly Color Plate = new Color32(18, 26, 42, 255);
        static readonly Color PlateLine = new Color32(38, 56, 86, 255);
        static readonly Color RingHot = new Color32(255, 140, 30, 255);
        static readonly Color RingCool = new Color32(70, 160, 255, 255);
        static readonly Color TankLight = new Color32(255, 176, 62, 255);
        static readonly Color TankMid = new Color32(232, 132, 24, 255);
        static readonly Color TankDark = new Color32(150, 78, 12, 255);
        static readonly Color Steel = new Color32(196, 208, 224, 255);
        static readonly Color TrackCol = new Color32(20, 26, 38, 255);
        static readonly Color WheelCol = new Color32(64, 82, 108, 255);
        static readonly Color FlashHot = new Color32(255, 246, 214, 255);
        static readonly Color FlashMid = new Color32(255, 178, 54, 255);

        static Color[] _px;

        public static void BuildAndAssignIcon()
        {
            _px = new Color[S * S];

            // ---------- background: radial gradient + vignette ----------
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    float dx = (x - S * 0.5f) / (S * 0.5f);
                    float dy = (y - S * 0.5f) / (S * 0.5f);
                    float d = Mathf.Clamp01(Mathf.Sqrt(dx * dx + dy * dy));
                    _px[y * S + x] = Color.Lerp(BgInner, BgOuter, d * d);
                }

            // ---------- badge plate ----------
            Disc(256, 256, 236, Color.Lerp(RingCool, RingHot, 0.15f), 1f);   // outer ring
            Disc(256, 256, 226, BgOuter, 1f);                                 // ring gap
            Disc(256, 256, 218, RingHot, 1f);                                 // hot inner ring
            Disc(256, 256, 209, Plate, 1f);                                   // plate face

            // subtle hex-plate hatching on the badge
            for (int y = 40; y < 472; y += 14)
                LineAA(48, y, 464, y - 26, 1.1f, PlateLine, 0.25f, clipR: 205);

            // ---------- muzzle flash behind the barrel ----------
            Glow(392, 268, 120, FlashMid, 0.55f);
            Glow(392, 268, 62, FlashHot, 0.75f);
            for (int i = 0; i < 9; i++)                       // flash spikes
            {
                float a = i * (Mathf.PI * 2f / 9f) + 0.25f;
                LineAA(392, 268,
                       392 + Mathf.Cos(a) * 118f, 268 + Mathf.Sin(a) * 118f,
                       7f, FlashMid, 0.5f, clipR: 205);
            }

            // ---------- ground shadow ----------
            Ellipse(250, 150, 168, 24, new Color(0f, 0f, 0f, 1f), 0.45f);

            // ---------- tracks ----------
            RoundRect(96, 138, 372, 206, 22, TrackCol, 1f);
            for (int i = 0; i < 5; i++)
                Disc(132 + i * 52, 172, 21, WheelCol, 1f);
            for (int i = 0; i < 5; i++)
                Disc(132 + i * 52, 172, 9, TrackCol, 1f);
            // track tread ticks
            for (int x = 104; x < 366; x += 22)
                RoundRect(x, 196, x + 11, 208, 3, WheelCol, 0.85f);

            // ---------- hull: angular wedge ----------
            Poly(new[]
            {
                new Vector2(92,  206), new Vector2(330, 206),
                new Vector2(392, 246), new Vector2(392, 286),
                new Vector2(92,  286)
            }, TankMid, 1f);
            // top highlight band
            Poly(new[]
            {
                new Vector2(92,  266), new Vector2(342, 266),
                new Vector2(384, 282), new Vector2(92,  282)
            }, TankLight, 1f);
            // dark front glacis
            Poly(new[]
            {
                new Vector2(330, 206), new Vector2(392, 246),
                new Vector2(392, 262), new Vector2(330, 226)
            }, TankDark, 1f);

            // ---------- turret ----------
            Poly(new[]
            {
                new Vector2(168, 286), new Vector2(316, 286),
                new Vector2(300, 356), new Vector2(190, 356)
            }, TankMid, 1f);
            Poly(new[]
            {
                new Vector2(190, 340), new Vector2(300, 340),
                new Vector2(296, 356), new Vector2(194, 356)
            }, TankLight, 1f);
            Disc(232, 336, 17, TankDark, 1f);          // hatch
            Disc(232, 336, 9, TankMid, 1f);

            // ---------- barrel ----------
            RoundRect(312, 300, 452, 328, 8, TankMid, 1f);
            RoundRect(312, 318, 452, 328, 5, TankLight, 1f);
            RoundRect(436, 292, 470, 336, 7, Steel, 1f);   // muzzle brake
            RoundRect(444, 300, 462, 328, 4, TankDark, 1f);

            // antenna
            LineAA(196, 356, 176, 428, 4f, Steel, 0.9f);
            Disc(176, 430, 6, RingHot, 1f);

            // ---------- corner targeting ticks ----------
            Color tick = new Color(1f, 1f, 1f, 1f);
            TickMark(96, 96, 1, 1, tick);
            TickMark(416, 96, -1, 1, tick);
            TickMark(96, 416, 1, -1, tick);
            TickMark(416, 416, -1, -1, tick);

            // ---------- top-left sheen ----------
            Glow(150, 392, 130, Color.white, 0.10f);

            WriteAsset();
        }

        static void TickMark(int x, int y, int sx, int sy, Color c)
        {
            LineAA(x, y, x + 34 * sx, y, 5f, c, 0.5f, clipR: 205);
            LineAA(x, y, x, y + 34 * sy, 5f, c, 0.5f, clipR: 205);
        }

        static void WriteAsset()
        {
            var tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
            var out32 = new Color32[S * S];
            for (int i = 0; i < _px.Length; i++)
            {
                var c = _px[i];
                out32[i] = new Color32(
                    (byte)Mathf.Clamp(Mathf.RoundToInt(c.r * 255f), 0, 255),
                    (byte)Mathf.Clamp(Mathf.RoundToInt(c.g * 255f), 0, 255),
                    (byte)Mathf.Clamp(Mathf.RoundToInt(c.b * 255f), 0, 255),
                    255);
            }
            tex.SetPixels32(out32);
            tex.Apply();

            if (!AssetDatabase.IsValidFolder(IconDir))
                AssetDatabase.CreateFolder("Assets", "Icons");
            File.WriteAllBytes(IconPath, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(IconPath, ImportAssetOptions.ForceUpdate);

            var importer = AssetImporter.GetAtPath(IconPath) as TextureImporter;
            if (importer != null)
            {
                importer.textureType = TextureImporterType.Default;
                importer.alphaIsTransparency = true;
                importer.mipmapEnabled = false;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.maxTextureSize = 512;
                importer.SaveAndReimport();
            }

            var icon = AssetDatabase.LoadAssetAtPath<Texture2D>(IconPath);
            PlayerSettings.SetIcons(NamedBuildTarget.Unknown, new[] { icon }, IconKind.Any);
            Debug.Log("[TankBattle] App icon generated and assigned.");
        }

        // ------------------------------------------------------------- drawing
        // All helpers blend with coverage so edges come out anti-aliased.

        static void Blend(int x, int y, Color c, float a)
        {
            if (a <= 0f || x < 0 || x >= S || y < 0 || y >= S) return;
            int i = y * S + x;
            _px[i] = Color.Lerp(_px[i], c, Mathf.Clamp01(a));
        }

        /// <summary>Anti-aliased filled circle.</summary>
        static void Disc(float cx, float cy, float r, Color c, float alpha)
        {
            int x0 = Mathf.FloorToInt(cx - r - 1), x1 = Mathf.CeilToInt(cx + r + 1);
            int y0 = Mathf.FloorToInt(cy - r - 1), y1 = Mathf.CeilToInt(cy + r + 1);
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    float d = Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
                    Blend(x, y, c, Mathf.Clamp01(r - d + 0.5f) * alpha);
                }
        }

        static void Ellipse(float cx, float cy, float rx, float ry, Color c, float alpha)
        {
            for (int y = Mathf.FloorToInt(cy - ry - 1); y <= Mathf.CeilToInt(cy + ry + 1); y++)
                for (int x = Mathf.FloorToInt(cx - rx - 1); x <= Mathf.CeilToInt(cx + rx + 1); x++)
                {
                    float nx = (x - cx) / rx, ny = (y - cy) / ry;
                    float d = Mathf.Sqrt(nx * nx + ny * ny);
                    Blend(x, y, c, Mathf.Clamp01((1f - d) * 8f) * alpha);
                }
        }

        /// <summary>Axis-aligned rounded rectangle (x0,y0)-(x1,y1).</summary>
        static void RoundRect(float x0, float y0, float x1, float y1, float radius,
                              Color c, float alpha)
        {
            float cx0 = x0 + radius, cx1 = x1 - radius;
            float cy0 = y0 + radius, cy1 = y1 - radius;
            for (int y = Mathf.FloorToInt(y0 - 1); y <= Mathf.CeilToInt(y1 + 1); y++)
                for (int x = Mathf.FloorToInt(x0 - 1); x <= Mathf.CeilToInt(x1 + 1); x++)
                {
                    float qx = Mathf.Max(Mathf.Max(cx0 - x, 0f), Mathf.Max(x - cx1, 0f));
                    float qy = Mathf.Max(Mathf.Max(cy0 - y, 0f), Mathf.Max(y - cy1, 0f));
                    float d = Mathf.Sqrt(qx * qx + qy * qy) - radius;
                    Blend(x, y, c, Mathf.Clamp01(-d + 0.5f) * alpha);
                }
        }

        /// <summary>Filled convex polygon (even-odd scanline with 2x2 sampling).</summary>
        static void Poly(Vector2[] pts, Color c, float alpha)
        {
            float minY = float.MaxValue, maxY = float.MinValue;
            float minX = float.MaxValue, maxX = float.MinValue;
            foreach (var p in pts)
            {
                minY = Mathf.Min(minY, p.y); maxY = Mathf.Max(maxY, p.y);
                minX = Mathf.Min(minX, p.x); maxX = Mathf.Max(maxX, p.x);
            }

            for (int y = Mathf.FloorToInt(minY) - 1; y <= Mathf.CeilToInt(maxY) + 1; y++)
                for (int x = Mathf.FloorToInt(minX) - 1; x <= Mathf.CeilToInt(maxX) + 1; x++)
                {
                    int hits = 0;
                    for (int sy = 0; sy < 2; sy++)
                        for (int sx = 0; sx < 2; sx++)
                            if (InPoly(pts, x + 0.25f + sx * 0.5f, y + 0.25f + sy * 0.5f)) hits++;
                    if (hits > 0) Blend(x, y, c, (hits / 4f) * alpha);
                }
        }

        static bool InPoly(Vector2[] pts, float px, float py)
        {
            bool inside = false;
            for (int i = 0, j = pts.Length - 1; i < pts.Length; j = i++)
            {
                if ((pts[i].y > py) != (pts[j].y > py) &&
                    px < (pts[j].x - pts[i].x) * (py - pts[i].y) /
                         (pts[j].y - pts[i].y) + pts[i].x)
                    inside = !inside;
            }
            return inside;
        }

        /// <summary>
        /// Anti-aliased thick line. clipR > 0 keeps it inside the badge circle,
        /// which is how the hatching and corner ticks stay on the plate.
        /// </summary>
        static void LineAA(float ax, float ay, float bx, float by, float width,
                           Color c, float alpha, float clipR = 0f)
        {
            float half = width * 0.5f;
            int x0 = Mathf.FloorToInt(Mathf.Min(ax, bx) - half - 1);
            int x1 = Mathf.CeilToInt(Mathf.Max(ax, bx) + half + 1);
            int y0 = Mathf.FloorToInt(Mathf.Min(ay, by) - half - 1);
            int y1 = Mathf.CeilToInt(Mathf.Max(ay, by) + half + 1);

            float dx = bx - ax, dy = by - ay;
            float len2 = dx * dx + dy * dy;

            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    if (clipR > 0f)
                    {
                        float rr = Mathf.Sqrt((x - 256f) * (x - 256f) + (y - 256f) * (y - 256f));
                        if (rr > clipR) continue;
                    }
                    float t = len2 > 0f
                        ? Mathf.Clamp01(((x - ax) * dx + (y - ay) * dy) / len2) : 0f;
                    float px2 = ax + dx * t, py2 = ay + dy * t;
                    float d = Mathf.Sqrt((x - px2) * (x - px2) + (y - py2) * (y - py2));
                    Blend(x, y, c, Mathf.Clamp01(half - d + 0.5f) * alpha);
                }
        }

        /// <summary>Soft radial glow.</summary>
        static void Glow(float cx, float cy, float r, Color c, float strength)
        {
            for (int y = Mathf.FloorToInt(cy - r); y <= Mathf.CeilToInt(cy + r); y++)
                for (int x = Mathf.FloorToInt(cx - r); x <= Mathf.CeilToInt(cx + r); x++)
                {
                    float d = Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
                    if (d > r) continue;
                    float a = Mathf.Pow(1f - d / r, 2f) * strength;
                    Blend(x, y, c, a);
                }
        }
    }
}
