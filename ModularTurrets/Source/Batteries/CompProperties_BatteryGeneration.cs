using System.Collections.Generic;
using RimWorld;
using Verse;

namespace ModularTurrets.Batteries;

public sealed class CompProperties_BatteryGeneration : CompProperties_Power
{
    public CompProperties_BatteryGeneration()
    {
        compClass = typeof(CompBatteryGeneration);
        showPowerNeededIfOff = false;
    }
}
