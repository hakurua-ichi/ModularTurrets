using System;
using UnityEngine;
using Verse;

namespace ModularTurrets;

/// <summary>Use a file-backed item outline while preserving the installed part material.</summary>
public sealed class Graphic_LoosePartOutline : Graphic_Single
{
    private GraphicRequest originalRequest;
    private Graphic_Single outlineGraphic;
    private Graphic_Single paragonGraphic, paragonOutlineGraphic;
    private bool atlasChecked, atlasAvailable;

    public static string OutlinePath(string sourcePath)
        => "ModularTurrets/ItemOutlines/" + sourcePath.Substring("ModularTurrets/".Length);

    public override void Init(GraphicRequest req)
    {
        base.Init(req);
        originalRequest = req;
        if (string.IsNullOrEmpty(path) || !path.StartsWith("ModularTurrets/", StringComparison.Ordinal)) return;
        // A different mod may replace the material with a masked shader. Keep
        // its original result rather than applying an unmatched outline mask.
        if (mat.HasProperty(ShaderPropertyIDs.MaskTex) && mat.GetTexture(ShaderPropertyIDs.MaskTex) != null) return;
        paragonGraphic = ParagonVisuals.PrebakedGraphicFor(req);
        Texture2D texture = ContentFinder<Texture2D>.Get(OutlinePath(path), reportFailure: false);
        if (texture == null || texture == BaseContent.BadTex || texture.width >= 512 || texture.height >= 512
            || string.IsNullOrEmpty(texture.CoreTexture?.SourcePath))
        {
            Log.Warning(L.T("MT_ItemOutlineUnavailable", path));
            return;
        }
        GraphicRequest outlined = req;
        outlined.graphicClass = typeof(Graphic_Single);
        outlined.path = OutlinePath(path);
        outlined.texture = texture;
        outlineGraphic = new Graphic_Single();
        outlineGraphic.Init(outlined);
        paragonOutlineGraphic = ParagonVisuals.PrebakedGraphicFor(outlined);
    }

    public override void TryInsertIntoAtlas(TextureAtlasGroup groupKey)
    {
        base.TryInsertIntoAtlas(groupKey);
        if (groupKey == TextureAtlasGroup.Item)
        {
            outlineGraphic?.TryInsertIntoAtlas(groupKey);
            paragonGraphic?.TryInsertIntoAtlas(groupKey);
            paragonOutlineGraphic?.TryInsertIntoAtlas(groupKey);
        }
    }

    public override Material MatAt(Rot4 rot, Thing thing = null)
    {
        if (outlineGraphic == null || thing?.Spawned != true || thing.def.category != ThingCategory.Item)
            return PartMaterial(base.MatAt(rot, thing), thing);
        if (!atlasChecked)
        {
            // Do not introduce an individual texture draw when pre-bake
            // registration failed. A fresh Graphic after reload checks again.
            atlasAvailable = GlobalTextureAtlasManager.TryGetStaticTile(TextureAtlasGroup.Item,
                (Texture2D)outlineGraphic.MatSingle.mainTexture, out _);
            atlasChecked = true;
        }
        return PartMaterial(atlasAvailable ? outlineGraphic.MatAt(rot, thing) : base.MatAt(rot, thing), thing);
    }

    private Material PartMaterial(Material source, Thing thing)
        => ParagonVisuals.IsParagon(thing) ? ParagonVisuals.MaterialFor(source, path) : source;

    public override Graphic GetColoredVersion(Shader newShader, Color newColor, Color newColorTwo)
        => GraphicDatabase.Get(typeof(Graphic_LoosePartOutline), path, newShader, drawSize,
            newColor, newColorTwo, data, originalRequest.shaderParameters, maskPath);
}
