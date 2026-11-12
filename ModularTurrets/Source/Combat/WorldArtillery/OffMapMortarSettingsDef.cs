using System;
using System.Collections.Generic;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace ModularTurrets;

public sealed class OffMapMortarSettingsDef : Def
{
    // Odyssey's buildable PassengerShuttle: min(floor(400 / 3), 62) = 62
    // on the surface. This remains available without requiring that DLC.
    public int maxWorldRange = 62;

    public override IEnumerable<string> ConfigErrors()
    {
        foreach (string error in base.ConfigErrors()) yield return error;
        if (maxWorldRange < 1) yield return L.T("MT_OffMapConfigWorldRange");
    }
}
