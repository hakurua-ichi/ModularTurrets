using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using Verse;

namespace ModularTurrets;

/// <summary>Provides ordinary item textures from the original, transparent 5x4 artwork.</summary>
public sealed class Graphic_PartAtlas : Graphic_Single
{
    public override void Init(GraphicRequest req)
    {
        string requestedPath = req.path ?? string.Empty;
        int slash = requestedPath.LastIndexOf('/');
        if (!int.TryParse(requestedPath.Substring(slash + 1), out int index))
        {
            Log.Error("[Modular Turrets] Invalid atlas graphic path: " + requestedPath);
            req.texture = BaseContent.BadTex;
        }
        else req.texture = PartAtlas.TextureAt(index);
        base.Init(req);
    }

    public override Graphic GetColoredVersion(Shader newShader, Color newColor, Color newColorTwo)
        => GraphicDatabase.Get<Graphic_PartAtlas>(path, newShader, drawSize, newColor, newColorTwo, data);
}
