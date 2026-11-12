using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace ModularTurrets;

public readonly struct CenteredIconLayout
{
    public readonly int Width, Height, OffsetX, OffsetY, TextureWidth, TextureHeight;
    public readonly int VisibleWidth, VisibleHeight;
    public readonly float IconScale;

    public CenteredIconLayout(int width, int height, int left, int bottom, int right, int top)
    {
        Width = width; Height = height;
        VisibleWidth = right - left; VisibleHeight = top - bottom;
        OffsetX = (width - VisibleWidth) / 2 - left;
        OffsetY = (height - VisibleHeight) / 2 - bottom;
        double scale = Math.Min(1d, (double)CenteredIconPixels.MaximumDimension / Math.Max(width, height));
        TextureWidth = Math.Max(1, (int)Math.Floor(width * scale + 0.5d));
        TextureHeight = Math.Max(1, (int)Math.Floor(height * scale + 0.5d));
        float target = CenteredIconPixels.ThumbnailVisibleFraction * Math.Max(width, height) / Math.Max(VisibleWidth, VisibleHeight);
        // Fit both axes: an off-center, tall part can live on a wide source canvas.
        float maximum = CenteredIconPixels.VisibleFraction * Math.Min((float)width / VisibleWidth, (float)height / VisibleHeight);
        IconScale = Math.Min(target, maximum);
    }
}
