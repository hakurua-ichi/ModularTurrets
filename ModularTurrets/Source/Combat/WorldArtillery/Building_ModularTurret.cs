using System;
using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace ModularTurrets;

public sealed partial class Building_ModularTurret
{
    internal void ResetOffMapAim()
    {
        targetCoordination?.ReleaseHead();
        foreach (var channel in channels)
            if (channel.Slot == "head")
            { channel.Verb?.Reset(); channel.Target = LocalTargetInfo.Invalid; channel.Warmup = 0; channel.QuickRetarget = false; }
    }
}
