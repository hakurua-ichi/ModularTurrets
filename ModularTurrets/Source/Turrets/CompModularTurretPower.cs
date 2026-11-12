using System.Collections.Generic;
using ModularTurrets.Batteries;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace ModularTurrets;

// Retain the public type for external packages compiled against earlier releases.
public sealed class CompModularTurretPower : CompPowerTrader
{
    public override void SetUpPowerVars()
    {
        base.SetUpPowerVars();
        if (parent is Building_ModularTurret { HasInternalSupply: true }) PowerOutput = 0f;
    }
}
