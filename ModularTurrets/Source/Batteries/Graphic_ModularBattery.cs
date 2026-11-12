using System;
using System.Runtime.CompilerServices;
using RimWorld;
using UnityEngine;
using Verse;

namespace ModularTurrets.Batteries;

// The Def texture already contains the plate and empty body. This also gives
// Core a single, correctly scaled image for blueprints and minified crates.
public sealed class Graphic_ModularBattery : Graphic_Single
{
    internal const float ModuleWidth = 3f / 2.98f;
    private static readonly Vector2 ModuleSize = new(ModuleWidth, 3f);

    public override Graphic GetColoredVersion(Shader newShader, Color newColor, Color newColorTwo) =>
        GraphicDatabase.Get<Graphic_ModularBattery>(path, newShader, drawSize, newColor, newColorTwo, data);

    public override void Print(SectionLayer layer, Thing thing, float extraRotation)
    {
        base.Print(layer, thing, extraRotation);
        if (thing is not Building_ModularBattery building) return;
        float angle = thing.Rotation.AsAngle + extraRotation;
        Vector3 center = thing.TrueCenter() + Altitudes.AltIncVect;
        PrintModule(layer, building.ModuleAt("left"), center, angle, false);
        PrintModule(layer, building.ModuleAt("right"), center, angle, true);
    }

    private static void PrintModule(SectionLayer layer, Thing part, Vector3 center, float angle, bool right)
    {
        if (part == null) return;
        float offset = (3f - ModuleWidth) * (right ? 0.5f : -0.5f);
        Vector3 at = center + new Vector3(offset, 0f, 0f).RotatedBy(angle);
        Material mat = part.Graphic.MatSingle;
        TryGetTextureAtlasReplacementInfo(mat, TextureAtlasGroup.Item, right, false, out mat, out var uvs, out _);
        Printer_Plane.PrintPlane(layer, at, ModuleSize, mat, angle, right, uvs);
    }
}
