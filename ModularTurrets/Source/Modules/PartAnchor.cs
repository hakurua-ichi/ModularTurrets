using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace ModularTurrets;

public sealed class PartAnchor
{
    public string slot;
    public Vector2 pivot = new(0.5f, 0.5f);
    public float angle;
    // Mirror before rotating; pivots remain in the original image coordinates.
    public bool flipX;
    public GraphicData graphicData;
    // Game seconds (60 ticks); zero preserves the ordinary body-following graphic.
    public float rotationDegreesPerSecond;
    public GraphicData fixedGraphicData;
    public Vector2 fixedPivot = new(0.5f, 0.5f);
    public PartBeam beam;
    public FleckDef muzzleFlash;
    // Main layer's original image UVs; selected once per successful trigger shot.
    public List<Vector2> muzzles = new();
}
