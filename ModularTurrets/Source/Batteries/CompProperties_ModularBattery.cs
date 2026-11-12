using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace ModularTurrets.Batteries;

public sealed class CompProperties_ModularBattery : CompProperties_Battery
{
    public CompProperties_ModularBattery()
    {
        compClass = typeof(CompModularBattery);
        storedEnergyMax = 1200f;
        efficiency = 0.8f;
        shortCircuitInRain = false;
        transmitsPower = true;
    }

    internal CompProperties_ModularBattery CopyForInstance()
        => (CompProperties_ModularBattery)MemberwiseClone();
}
