using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;
using ShaderRegistry = Waylith.Rendering.ShaderRegistry;

namespace ModularTurrets;

/// <summary>Grade-specific housing pixels; never modifies shared originals or world textures.</summary>
internal static class ParagonVisuals
{
    private sealed class CachedMaterial
    {
        internal int Version;
        internal Material Result;
    }
    private static readonly Dictionary<Material, CachedMaterial> Materials = new();
    private static readonly Dictionary<ThingDef, Texture2D> Icons = new();
    private static bool shaderChecked;
    private static Shader housingShader;
    private static bool warned;
    // Pixel-space ROI surrounds FireControl's orange indicator, including antialiasing.
    private static readonly Vector4 Indicator = new(300f / 1263f, 1f - 685f / 1246f, 330f / 1263f, 1f - 654f / 1246f);

    private static bool HousingPath(string path) => path is
        "ModularTurrets/Components/RangePod-left" or "ModularTurrets/Components/ShieldPod-right"
        or "ModularTurrets/Components/AuxiliaryPod-left" or "ModularTurrets/Components/AimingPod-left"
        or "ModularTurrets/Components/CoolingPod-left" or "ModularTurrets/Components/ServoPod-left"
        or "ModularTurrets/Components/FanPod-left";

    internal static bool IsParagon(Thing part)
        => part != null && part.def.modContentPack == ModularTurretsMod.Instance?.Content
            && HousingPath(part.def.graphicData?.texPath) && ParagonUtility.IsParagon(part);

    internal static bool Available
    {
        get
        {
            if (shaderChecked) return housingShader != null;
            shaderChecked = true;
            try
            {
                if (ShaderRegistry.TryResolve("Cutout", out var original)
                && !original.HasErrors
                && original.VertexShaderFileName == "SceneDefault.vert.glsl"
                && original.FragmentShaderFileName == "Cutout.frag.glsl")
                    housingShader = ShaderDatabase.Cutout;
            }
            catch (Exception error) { Warn(error); }
            if (housingShader == null) Warn("shader unavailable or changed");
            return housingShader != null;
        }
    }

    internal static Material MaterialFor(Material source, string sourcePath)
    {
        if (!HousingPath(sourcePath) || source == null || source.shader != ShaderDatabase.Cutout
            || source.passCount != 1 || source.renderQueue != ShaderDatabase.Cutout.renderQueue) return source;
        int version = source.engineMaterial.Version;
        if (Materials.TryGetValue(source, out var cached))
            // Respect an external mutation instead of reusing an obsolete clone.
            return cached.Version == version ? cached.Result : source;
        Material result = source;
        try
        {
            if (Available && source.mainTextureScale == Vector2.one && source.mainTextureOffset == Vector2.zero
                && MaterialPool.TryGetRequestForMat(source, out var request)
                && request.maskTex == null && request.secondaryTex == null
                && (request.shaderParameters == null || request.shaderParameters.Count == 0)
                && request.mainTex == source.mainTexture && request.color == source.color)
            {
                // Accept only our original full-canvas or item-outline texture. A modded
                // material/texture keeps its own result instead of receiving an unmatched recolor.
                string texturePath = sourcePath;
                if (source.mainTexture != ContentFinder<Texture2D>.Get(texturePath, reportFailure: false))
                    texturePath = Graphic_LoosePartOutline.OutlinePath(sourcePath);
                if (source.mainTexture == ContentFinder<Texture2D>.Get(texturePath, reportFailure: false))
                {
                    Texture2D texture = ContentFinder<Texture2D>.Get(TexturePath(texturePath), reportFailure: false);
                    if (texture != null && texture != BaseContent.BadTex
                        && texture.width == source.mainTexture.width && texture.height == source.mainTexture.height)
                    {
                        request.shader = housingShader;
                        request.mainTex = texture;
                        result = MaterialPool.MatFrom(request);
                    }
                }
            }
        }
        catch (Exception error) { Warn(error); }
        Materials[source] = new CachedMaterial { Version = version, Result = result };
        return result;
    }

    internal static string TexturePath(string sourcePath)
    {
        const string prefix = "ModularTurrets/";
        if (sourcePath == null || !sourcePath.StartsWith(prefix, StringComparison.Ordinal)) return null;
        string path = sourcePath.Replace("ModularTurrets/ItemOutlines/", prefix, StringComparison.Ordinal);
        return HousingPath(path) ? "ModularTurrets/Paragon/" + sourcePath.Substring(prefix.Length) : null;
    }

    // Register file-backed variants through the same pre-bake path as ordinary loose parts.
    // Runtime draw calls only select cached materials; no world pixels are read or converted.
    internal static Graphic_Single PrebakedGraphicFor(GraphicRequest request)
    {
        string path = TexturePath(request.path);
        if (path == null || request.shader != ShaderDatabase.Cutout) return null;
        Texture2D texture = ContentFinder<Texture2D>.Get(path, reportFailure: false);
        if (texture == null || texture == BaseContent.BadTex) return null;
        request.path = path;
        request.texture = texture;
        request.graphicClass = typeof(Graphic_Single);
        var graphic = new Graphic_Single();
        graphic.Init(request);
        return graphic;
    }

    internal static void TransformPixels(Color32[] pixels, int width, int height, string path)
    {
        if (!HousingPath(path)) return;
        bool indicator = path == "ModularTurrets/Components/FanPod-left";
        for (int y = 0; y < height; y++) for (int x = 0; x < width; x++)
        {
            if (indicator && (x + .5f) / width >= Indicator.x && (x + .5f) / width <= Indicator.z
                && (y + .5f) / height >= Indicator.y && (y + .5f) / height <= Indicator.w) continue;
            int index = y * width + x;
            Color32 pixel = pixels[index];
            float r = pixel.r / 255f, g = pixel.g / 255f, b = pixel.b / 255f, span = r - b;
            if (span <= .04f || r - g <= .01f || g - b <= .02f || g - b < span * .40f
                || g - b > span * .80f || span < r * .12f) continue;
            float luminance = .2126f * r + .7152f * g + .0722f * b;
            pixels[index] = new Color32(Byte(luminance * .50f), Byte(luminance * .283f), Byte(luminance * .678f), pixel.a);
        }
    }

    internal static Texture2D IconFor(ThingDef definition)
    {
        if (!Available) return null;
        if (Icons.TryGetValue(definition, out var result)) return result;
        Texture2D created = null;
        try
        {
            if (definition.graphicData?.Graphic?.Shader != ShaderDatabase.Cutout) return null;
            string path = definition.uiIconPath;
            if (!HousingPath(path)) return null;
            Texture2D source = ContentFinder<Texture2D>.Get(path, reportFailure: false);
            if (source == null || source == BaseContent.BadTex) return null;
            Color32[] pixels = CenteredPartIcons.ReadPixels(source);
            if (!CenteredIconPixels.TryGetLayout(pixels, source.width, source.height, out var layout)) return null;
            TransformPixels(pixels, source.width, source.height, path);
            created = new Texture2D(layout.TextureWidth, layout.TextureHeight, TextureFormat.RGBA32, false)
            {
                name = "MT_ParagonUI_" + definition.defName,
                filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp
            };
            created.SetPixels32(CenteredIconPixels.CreateThumbnail(pixels, layout));
            created.Apply(false, true);
            result = created;
        }
        catch (Exception error)
        {
            if (created != null) UnityEngine.Object.Destroy(created);
            Warn(error);
        }
        finally { Icons[definition] = result; }
        return result;
    }

    private static byte Byte(float value) => (byte)Math.Clamp((int)MathF.Floor(value * 255f + .5f), 0, 255);
    private static void Warn(Exception error)
    {
        if (warned) return;
        warned = true;
        ModDiagnostics.WarningOnce("Rendering.ParagonVisuals", error);
    }

    private static void Warn(string detail)
    {
        if (warned) return;
        warned = true;
        Log.Warning(L.T("MT_ParagonVisualFallback", detail));
    }
}
