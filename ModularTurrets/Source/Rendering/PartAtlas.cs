using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using Verse;

namespace ModularTurrets;

public static class PartAtlas
{
    private static readonly Dictionary<int, Texture2D> Tiles = new();

    // Artwork pivots, measured downward from each cell's top edge. Head pivots are
    // the roof center; module pivots are their small attachment contact, not a base.
    public static float PivotY(int index) => index switch
    {
        2 => 0.77f, 3 => 0.77f, 4 => 0.77f,
        5 => 0.75f, 6 => 0.75f, 7 => 0.75f, 8 => 0.75f, 9 => 0.75f,
        10 => 0.69f, 11 => 0.62f, 12 => 0.70f,
        13 => 0.83f, 14 => 0.83f, 15 => 0.85f, 16 => 0.82f, 17 => 0.86f,
        _ => 0.5f
    };

    public static Texture2D TextureAt(int index)
    {
        // Equipped gun icons use the 5x4 head sheet. Modules and mounts now have
        // individual textures; the retired concept atlases are not runtime inputs.
        if (index < 0 || index >= 20) return BaseContent.BadTex;
        if (Tiles.TryGetValue(index, out Texture2D existing)) return existing;
        const int columns = 5, rows = 4;
        Texture2D atlas = ContentFinder<Texture2D>.Get("ModularTurrets/PartsAtlas");
        if (atlas == null || atlas.width < columns || atlas.height < rows) return BaseContent.BadTex;

        int column = index % columns;
        int rowFromTop = index / columns;
        // Proportional edges preserve all pixels even when the image dimensions are odd.
        int left = column * atlas.width / columns;
        int right = (column + 1) * atlas.width / columns;
        int bottom = (rows - rowFromTop - 1) * atlas.height / rows;
        int top = (rows - rowFromTop) * atlas.height / rows;
        int width = right - left;
        int height = top - bottom;
        Texture2D tile = new Texture2D(width, height, TextureFormat.RGBA32, false)
        {
            name = "MT_Part_" + index,
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp
        };
        try
        {
            tile.SetPixels(atlas.GetPixels(left, bottom, width, height));
        }
        catch (Exception)
        {
            // Content loaders may discard CPU pixels. Restore readable pixels on demand.
            RenderTexture previous = RenderTexture.active;
            RenderTexture copy = RenderTexture.GetTemporary(atlas.width, atlas.height, 0);
            try
            {
                Graphics.Blit(atlas, copy);
                RenderTexture.active = copy;
                tile.ReadPixels(new Rect(left, bottom, width, height), 0, 0, false);
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(copy);
            }
        }
        tile.Apply(false, false);
        Tiles.Add(index, tile);
        return tile;
    }
}
