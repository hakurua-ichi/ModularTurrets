using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace ModularTurrets;

public sealed class TurretWreckStateDef : Def
{
    public int additionalHitPoints = 1000;
    public float restorationWork = 2500f;

    public override IEnumerable<string> ConfigErrors()
    {
        foreach (string error in base.ConfigErrors()) yield return error;
        if (additionalHitPoints < 1) yield return L.T("MT_ConfigWreckHitPoints");
        if (!float.IsFinite(restorationWork) || restorationWork <= 0f)
            yield return L.T("MT_ConfigRestorationWork");
    }
}
