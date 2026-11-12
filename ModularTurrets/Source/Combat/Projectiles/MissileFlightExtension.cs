using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace ModularTurrets;

public sealed class MissileFlightExtension : DefModExtension
{
    public float curveWidth = 4.5f;
    public float distanceWidthLimit = 0.25f;
    public float durationMultiplier = 1.35f;
    public int smokeInterval = 2;
    public float smokeSize = 0.35f;

    public override IEnumerable<string> ConfigErrors()
    {
        if (!float.IsFinite(curveWidth) || curveWidth < 0f) yield return L.T("MT_ConfigCurveWidth");
        if (!float.IsFinite(distanceWidthLimit) || distanceWidthLimit < 0f) yield return L.T("MT_ConfigDistanceWidthLimit");
        if (!float.IsFinite(durationMultiplier) || durationMultiplier <= 0f) yield return L.T("MT_ConfigDurationMultiplier");
        if (smokeInterval < 1) yield return L.T("MT_ConfigSmokeInterval");
        if (!float.IsFinite(smokeSize) || smokeSize < 0f) yield return L.T("MT_ConfigSmokeSize");
    }
}
