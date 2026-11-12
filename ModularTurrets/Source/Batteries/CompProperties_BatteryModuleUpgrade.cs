using System;
using RimWorld;
using Verse;

namespace ModularTurrets.Batteries;

public sealed class CompProperties_BatteryModuleUpgrade : CompProperties
{
    public CompProperties_BatteryModuleUpgrade() => compClass = typeof(CompBatteryModuleUpgrade);
}
