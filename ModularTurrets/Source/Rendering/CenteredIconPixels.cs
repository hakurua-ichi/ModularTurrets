using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace ModularTurrets;

/// <summary>Pure pixel/layout operations, independent of Def loading or graphics initialization.</summary>
public static class CenteredIconPixels
{
    public const byte VisibleAlpha = 8;
    public const float VisibleFraction = 0.94f;
    public const int MaximumDimension = 512;
    internal const float ThumbnailVisibleFraction = 0.75f;

    public static bool TryGetLayout(Color32[] source, int width, int height, out CenteredIconLayout layout)
    {
        layout = default;
        if (source == null || width <= 0 || height <= 0 || (long)width * height != source.LongLength) return false;
        int left = width, bottom = height, right = 0, top = 0;
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                if (source[y * width + x].a < VisibleAlpha) continue;
                left = Math.Min(left, x); right = Math.Max(right, x + 1);
                bottom = Math.Min(bottom, y); top = Math.Max(top, y + 1);
            }
        }
        if (right <= left || top <= bottom) return false;
        layout = new CenteredIconLayout(width, height, left, bottom, right, top);
        return true;
    }

    public static Color32[] CreateThumbnail(Color32[] source, CenteredIconLayout layout)
    {
        if (source == null || layout.Width <= 0 || layout.Height <= 0
            || (long)layout.Width * layout.Height != source.LongLength) throw new ArgumentException(nameof(source));
        var result = new Color32[layout.TextureWidth * layout.TextureHeight];
        // Bake the visible-content scale into the UI texture. Menus that draw uiIcon
        // directly cannot apply the separate Def scale used by Core's ThingIcon.
        double stepX = (double)layout.Width / layout.TextureWidth / layout.IconScale;
        double stepY = (double)layout.Height / layout.TextureHeight / layout.IconScale;
        double centerX = (layout.Width - 1) * 0.5d - layout.OffsetX;
        double centerY = (layout.Height - 1) * 0.5d - layout.OffsetY;
        for (int y = 0; y < layout.TextureHeight; y++)
        {
            double sy = centerY + (y + 0.5d - layout.TextureHeight * 0.5d) * stepY;
            int y0 = (int)Math.Floor(sy);
            float fy = (float)(sy - y0);
            for (int x = 0; x < layout.TextureWidth; x++)
            {
                // Keep the original canvas aspect ratio so Core's graphic proportions agree.
                double sx = centerX + (x + 0.5d - layout.TextureWidth * 0.5d) * stepX;
                int x0 = (int)Math.Floor(sx);
                float fx = (float)(sx - x0);
                result[y * layout.TextureWidth + x] = Bilinear(
                    PixelAt(source, layout, x0, y0), PixelAt(source, layout, x0 + 1, y0),
                    PixelAt(source, layout, x0, y0 + 1), PixelAt(source, layout, x0 + 1, y0 + 1), fx, fy);
            }
        }
        return result;
    }

    private static Color32 PixelAt(Color32[] source, CenteredIconLayout layout, int x, int y)
        => x < 0 || x >= layout.Width || y < 0 || y >= layout.Height ? default : source[y * layout.Width + x];

    internal static Color32 Sample(Color32[] source, int width, int height, float x, float y)
    {
        int x0 = (int)Math.Floor(x), y0 = (int)Math.Floor(y);
        Color32 Pixel(int px, int py) => px < 0 || px >= width || py < 0 || py >= height ? default : source[py * width + px];
        return Bilinear(Pixel(x0, y0), Pixel(x0 + 1, y0), Pixel(x0, y0 + 1), Pixel(x0 + 1, y0 + 1), x - x0, y - y0);
    }

    private static Color32 Bilinear(Color32 a, Color32 b, Color32 c, Color32 d, float x, float y)
    {
        float wa = (1f - x) * (1f - y) * a.a, wb = x * (1f - y) * b.a;
        float wc = (1f - x) * y * c.a, wd = x * y * d.a;
        float alpha = wa + wb + wc + wd;
        if (alpha <= 0f) return default;
        // Premultiplied interpolation prevents hidden RGB in transparent pixels from darkening edges.
        return new Color32(ToByte((a.r * wa + b.r * wb + c.r * wc + d.r * wd) / alpha),
            ToByte((a.g * wa + b.g * wb + c.g * wc + d.g * wd) / alpha),
            ToByte((a.b * wa + b.b * wb + c.b * wc + d.b * wd) / alpha), ToByte(alpha));
    }

    private static byte ToByte(float value) => (byte)Math.Clamp((int)Math.Floor(value + 0.5f), 0, 255);
}
