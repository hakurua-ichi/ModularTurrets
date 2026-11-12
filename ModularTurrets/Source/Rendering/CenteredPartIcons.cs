using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace ModularTurrets;

/// <summary>Center only this mod's UI icons; world graphics and source textures stay unchanged.</summary>
[StaticConstructorOnStartup]
public static class CenteredPartIcons
{
    private sealed class CachedIcon
    {
        public Texture2D Texture;
    }

    private static readonly Dictionary<Texture2D, CachedIcon> Cache = new();
    private static readonly HashSet<Texture2D> Generated = new();

    internal static bool OwnsIcon(Texture texture) => texture is Texture2D image && Generated.Contains(image);

    static CenteredPartIcons()
    {
        ModContentPack content = ModularTurretsMod.Instance?.Content;
        if (content == null) return;
        foreach (ThingDef definition in DefDatabase<ThingDef>.AllDefsListForReading)
        {
            if (definition.modContentPack != content) continue;
            if (definition.GetModExtension<PartExtension>() == null
                && definition.GetModExtension<Batteries.BatteryModuleExtension>() == null
                && definition.defName != "MT_TurretBase" && definition.defName != "MT_TurretBaseLarge"
                && definition.defName != "MT_AssemblyBench") continue;
            try { Apply(definition); }
            catch (Exception error) { Log.Warning(L.T("MT_IconCenteringFailed", definition.defName, error.Message)); }
        }
    }

    private static void Apply(ThingDef definition)
    {
        Texture2D source = definition.uiIcon;
        if (source == null || source == BaseContent.BadTex) return;
        // Explicit uiIconPath bypasses Core's material-based icon resolver for our definitions.
        // Preserve an unexpected externally supplied UI material instead of mutating a pooled one.
        if (definition.uiIconMaterial != null)
        {
            Log.Warning(L.T("MT_IconCenteringMaterialUnsupported", definition.defName));
            return;
        }
        if (!Cache.TryGetValue(source, out CachedIcon icon))
        {
            Color32[] pixels = ReadPixels(source);
            if (!CenteredIconPixels.TryGetLayout(pixels, source.width, source.height, out CenteredIconLayout layout)) return;
            Texture2D centered = new Texture2D(layout.TextureWidth, layout.TextureHeight, TextureFormat.RGBA32, false)
            {
                name = "MT_UI_" + source.name,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            try
            {
                centered.SetPixels32(CenteredIconPixels.CreateThumbnail(pixels, layout));
                // The cached icon only needs its GPU copy after initialization.
                centered.Apply(false, true);
            }
            catch
            {
                UnityEngine.Object.Destroy(centered);
                throw;
            }
            icon = new CachedIcon { Texture = centered };
            Cache.Add(source, icon);
            Generated.Add(centered);
        }
        definition.uiIcon = icon.Texture;
        definition.uiIconScale = 1f;
        definition.uiIconOffset = Vector2.zero;
    }

    internal static Color32[] ReadPixels(Texture2D source)
    {
        if (!source.isReadable) return ReadGpuPixels(source);
        try { return source.GetPixels32(); }
        catch (Exception error) when (error is UnityException or InvalidOperationException)
        {
            return ReadGpuPixels(source);
        }
    }

    private static Color32[] ReadGpuPixels(Texture2D source)
    {
        RenderTexture previous = RenderTexture.active;
        RenderTexture copy = RenderTexture.GetTemporary(source.width, source.height, 0);
        Texture2D readable = null;
        try
        {
            Graphics.Blit(source, copy);
            RenderTexture.active = copy;
            readable = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false);
            readable.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0, false);
            readable.Apply(false, false);
            return readable.GetPixels32();
        }
        finally
        {
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(copy);
            if (readable != null) UnityEngine.Object.Destroy(readable);
        }
    }
}
