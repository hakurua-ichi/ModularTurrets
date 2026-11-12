using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace ModularTurrets;

public sealed class PartBeam
{
    // Original image UV, measured from its top-left, before mirror/rotation.
    public Vector2 start = new(0.5f, 0f);
    public float angle;
    public float length = 1f, width = 0.015f;
    public Color color = new(1f, 0.03f, 0.03f, 1f);
    public float fadePower = 1.5f;

    public IEnumerable<string> ConfigErrors()
    {
        if (!float.IsFinite(start.x) || !float.IsFinite(start.y) || !float.IsFinite(angle)) yield return L.T("MT_ConfigBeamStartAngle");
        if (!float.IsFinite(length) || length <= 0f || !float.IsFinite(width) || width <= 0f) yield return L.T("MT_ConfigBeamSize");
        if (!float.IsFinite(fadePower) || fadePower <= 0f) yield return L.T("MT_ConfigBeamFadePower");
        if (!ValidColor(color.r) || !ValidColor(color.g) || !ValidColor(color.b) || !ValidColor(color.a)) yield return L.T("MT_ConfigBeamColor");
    }
    private static bool ValidColor(float value) => float.IsFinite(value) && value >= 0f && value <= 1f;
}
