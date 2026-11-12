using System;
using System.Collections.Generic;
using Verse;

namespace ModularTurrets.Batteries;

public sealed class BatteryModuleExtension : DefModExtension
{
    public float efficiencyBonus;
    public float chargeEfficiencyFloor;
    public float additionalCapacityWd;
    public float upgradeCapacityBonusFactor;
    public float generationWattsPerWd;
    public float generationMinimumWatts;
    public float wirelessRadius;
    public float reserveFraction;
    public bool protectsFromShortCircuit;
    public bool preventsBreakdown;
    public int maxUpgradeLevel;
    public float wirelessRadiusPerLevel;
    public float generationBonusPerLevel;
    public int upgradeSteelPerLevel = 40;
    public int upgradeComponentsPerLevel = 1;
    public float upgradeWorkPerLevel = 300f;

    private static float Amount(float value) => float.IsFinite(value) && value > 0f ? value : 0f;
    internal int MaximumLevel => Math.Max(0, maxUpgradeLevel);

    public float GenerationWattsAtLevel(float mainCapacity, int level)
        => Math.Max(Amount(generationMinimumWatts), Amount(generationWattsPerWd) * mainCapacity)
            * (1f + Math.Clamp(level, 0, MaximumLevel) * Amount(generationBonusPerLevel));

    public float WirelessRadiusAtLevel(int level)
        => Amount(wirelessRadius) + Math.Clamp(level, 0, MaximumLevel) * Amount(wirelessRadiusPerLevel);
}
