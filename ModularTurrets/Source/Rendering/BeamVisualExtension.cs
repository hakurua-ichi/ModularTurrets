using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace ModularTurrets;

public sealed class BeamVisualExtension : DefModExtension
{
    public float width = 0.22f, coreWidth = 0.065f;
    public int durationTicks = 8;
    public Color coreColor = new(0.9f, 1f, 1f, 1f);
    public Color haloColor = new(0.12f, 0.75f, 1f, 0.65f);

    internal bool Valid => float.IsFinite(width) && width > 0f
        && float.IsFinite(coreWidth) && coreWidth > 0f && coreWidth < width
        && durationTicks > 0 && durationTicks <= 60 && ValidColor(coreColor) && ValidColor(haloColor);

    private static bool ValidColor(Color color)
        => ValidChannel(color.r) && ValidChannel(color.g) && ValidChannel(color.b) && ValidChannel(color.a);
    private static bool ValidChannel(float value) => float.IsFinite(value) && value >= 0f && value <= 1f;
}
