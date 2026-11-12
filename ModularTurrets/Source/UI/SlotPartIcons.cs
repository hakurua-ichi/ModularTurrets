using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace ModularTurrets;

/// <summary>Static slot previews; never changes a Def's item icon or world graphic.</summary>
[StaticConstructorOnStartup]
public static class SlotPartIcons
{
    private sealed class Layer
    {
        public Color32[] Pixels;
        public int Width, Height;
        public Vector2 Size, Pivot;
        public float Mirror, Cos, Sin;
        public Color Tint;

        public Vector2 Position(float u, float v)
        {
            float x = (u - Pivot.x) * Size.x * Mirror;
            float y = (v - (1f - Pivot.y)) * Size.y;
            // Texture rows point up; this is the world's clockwise yaw seen from above.
            return new Vector2(Cos * x + Sin * y, -Sin * x + Cos * y);
        }

        public Color32 Sample(float x, float y)
        {
            float localX = Cos * x - Sin * y, localY = Sin * x + Cos * y;
            float u = Pivot.x + localX / (Size.x * Mirror);
            float v = 1f - Pivot.y + localY / Size.y;
            return CenteredIconPixels.Sample(Pixels, Width, Height, u * Width - 0.5f, v * Height - 0.5f);
        }
    }

    private static readonly Dictionary<(ThingDef definition, string slot, bool paragon), Texture2D> Cache = new();

    public static void Draw(Rect rect, Thing part, string slot)
    {
        if (part == null) return;
        PartExtension extension = part.def.GetModExtension<PartExtension>();
        PartAnchor anchor = extension?.anchors?.FirstOrDefault(a => a.slot == slot);
        bool previewSlot = slot is "top" or "right"
            || slot == "left" && part.def.GetModExtension<SpecialistModuleExtension>() != null;
        if (extension?.isHead != false || !previewSlot || anchor == null)
        {
            Widgets.ThingIcon(rect, part);
            return;
        }
        bool paragon = ParagonVisuals.IsParagon(part) && ParagonVisuals.Available;
        var key = (part.def, slot, paragon);
        if (!Cache.TryGetValue(key, out Texture2D icon))
        {
            try { icon = Create(part, anchor, paragon); }
            catch (Exception error)
            {
                ModDiagnostics.WarningOnce("UI.SlotPartIcon", error);
            }
            // Failed or empty sources fall back once, without retries on every UI frame.
            Cache[key] = icon;
        }
        if (icon == null) Widgets.ThingIcon(rect, part);
        else Widgets.DrawTextureFitted(rect, icon, 1f);
    }

    private static Texture2D Create(Thing part, PartAnchor anchor, bool paragon)
    {
        var layers = new List<Layer>();
        float left = float.PositiveInfinity, bottom = float.PositiveInfinity;
        float right = float.NegativeInfinity, top = float.NegativeInfinity;
        void Add(Graphic graphic, Vector2 pivot)
        {
            if (graphic?.MatSingle?.mainTexture is not Texture2D source || source == BaseContent.BadTex) return;
            Vector2 size = graphic.drawSize;
            if (!float.IsFinite(size.x) || !float.IsFinite(size.y) || size.x <= 0f || size.y <= 0f
                || !float.IsFinite(pivot.x) || !float.IsFinite(pivot.y) || !float.IsFinite(anchor.angle)) return;
            Color32[] pixels = CenteredPartIcons.ReadPixels(source);
            if (!CenteredIconPixels.TryGetLayout(pixels, source.width, source.height, out CenteredIconLayout layout)) return;
            if (paragon && graphic.Shader == ShaderDatabase.Cutout)
                ParagonVisuals.TransformPixels(pixels, source.width, source.height, graphic.path);
            float angle = anchor.angle * Mathf.Deg2Rad;
            var layer = new Layer
            {
                Pixels = pixels, Width = source.width, Height = source.height, Size = size, Pivot = pivot,
                Mirror = anchor.flipX ? -1f : 1f, Cos = Mathf.Cos(angle), Sin = Mathf.Sin(angle), Tint = graphic.color
            };
            int x0 = (layout.Width - layout.VisibleWidth) / 2 - layout.OffsetX;
            int y0 = (layout.Height - layout.VisibleHeight) / 2 - layout.OffsetY;
            foreach (Vector2 uv in new[]
            {
                new Vector2((float)x0 / source.width, (float)y0 / source.height),
                new Vector2((float)(x0 + layout.VisibleWidth) / source.width, (float)y0 / source.height),
                new Vector2((float)x0 / source.width, (float)(y0 + layout.VisibleHeight) / source.height),
                new Vector2((float)(x0 + layout.VisibleWidth) / source.width, (float)(y0 + layout.VisibleHeight) / source.height)
            })
            {
                Vector2 p = layer.Position(uv.x, uv.y);
                left = Math.Min(left, p.x); right = Math.Max(right, p.x);
                bottom = Math.Min(bottom, p.y); top = Math.Max(top, p.y);
            }
            layers.Add(layer);
        }

        // Preserve the same contact point and layer order as TurretRenderer.DrawPart.
        // Rotation phase is zero so this remains a stable inventory-style preview.
        if (anchor.fixedGraphicData != null) Add(anchor.fixedGraphicData.Graphic, anchor.fixedPivot);
        Add(anchor.graphicData?.Graphic ?? part.Graphic, anchor.pivot);
        if (layers.Count == 0) return null;
        float extent = Math.Max(right - left, top - bottom);
        if (!float.IsFinite(extent) || extent <= 0f) return null;
        const int dimension = CenteredIconPixels.MaximumDimension;
        float worldPerPixel = extent / (dimension * CenteredIconPixels.VisibleFraction);
        float centerX = (left + right) * 0.5f, centerY = (bottom + top) * 0.5f;
        var result = new Color32[dimension * dimension];
        for (int y = 0; y < dimension; y++)
        {
            float worldY = centerY + (y + 0.5f - dimension * 0.5f) * worldPerPixel;
            for (int x = 0; x < dimension; x++)
            {
                float worldX = centerX + (x + 0.5f - dimension * 0.5f) * worldPerPixel;
                float red = 0f, green = 0f, blue = 0f, alpha = 0f;
                foreach (Layer layer in layers)
                {
                    Color32 pixel = layer.Sample(worldX, worldY);
                    float a = pixel.a / 255f * layer.Tint.a, remaining = 1f - a;
                    red = pixel.r / 255f * layer.Tint.r * a + red * remaining;
                    green = pixel.g / 255f * layer.Tint.g * a + green * remaining;
                    blue = pixel.b / 255f * layer.Tint.b * a + blue * remaining;
                    alpha = a + alpha * remaining;
                }
                if (alpha > 0f) result[y * dimension + x] = new Color32(Byte(red / alpha), Byte(green / alpha), Byte(blue / alpha), Byte(alpha));
            }
        }
        var texture = new Texture2D(dimension, dimension, TextureFormat.RGBA32, false)
        {
            name = "MT_SlotUI_" + part.def.defName + "_" + anchor.slot,
            filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp
        };
        try
        {
            texture.SetPixels32(result);
            texture.Apply(false, true);
            return texture;
        }
        catch { UnityEngine.Object.Destroy(texture); throw; }
    }

    private static byte Byte(float value) => (byte)Math.Clamp((int)Math.Floor(value * 255f + 0.5f), 0, 255);
}
