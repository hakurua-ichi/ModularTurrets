using System;
using System.Collections.Generic;
using Verse;

namespace ModularTurrets;

// Opt-in on each ordinary module. Main heads and specialist modules never qualify.
public sealed class ParagonModuleExtension : DefModExtension
{
    public float powerMultiplier = 8f;
    public float auxiliaryDamageMultiplier = 4f;
    public float auxiliaryBurstMultiplier = 2f;
    public float powerConsumptionMultiplier = 4f;
    public float work = 1500000f;
    public string research = "MT_InfiniteEngineering";
    public List<ThingDefCountClass> costs = new();
    public int detectionIntervalTicks = 3;
    public float emergencyShieldFraction = 0.2f;
    public int emergencyShieldCooldownTicks = 3600;
    public float defenseRadius = 8f;
    public float openingArmorBonus = 0.25f;
    public float burstIntervalFactor = 0.5f;
    public float retargetWarmupFactor = 0.5f;
    public float forcedMissRadiusFactor = 0.5f;

    public override IEnumerable<string> ConfigErrors()
    {
        foreach (string error in base.ConfigErrors()) yield return error;
        if (!Valid(powerMultiplier, 8f) || !Valid(auxiliaryDamageMultiplier, 4f)
            || !Valid(auxiliaryBurstMultiplier, 2f) || !Valid(powerConsumptionMultiplier, 4f))
            yield return L.T("MT_ConfigParagonMultipliers");
        if (!float.IsFinite(work) || work <= 0f) yield return L.T("MT_ConfigParagonWork");
        if (detectionIntervalTicks < 1 || detectionIntervalTicks > 15 || emergencyShieldCooldownTicks < 60
            || !float.IsFinite(emergencyShieldFraction) || emergencyShieldFraction <= 0f || emergencyShieldFraction > 0.2f
            || !float.IsFinite(defenseRadius) || defenseRadius <= 0f || defenseRadius > 8f
            || !float.IsFinite(openingArmorBonus) || openingArmorBonus < 0f || openingArmorBonus > 0.25f
            || !UnitFactor(burstIntervalFactor) || !UnitFactor(retargetWarmupFactor) || !UnitFactor(forcedMissRadiusFactor))
            yield return L.T("MT_ConfigParagonFeatures");
        if (string.IsNullOrEmpty(research) || DefDatabase<ResearchProjectDef>.GetNamedSilentFail(research) == null)
            yield return L.T("MT_ConfigParagonResearch");
        if (costs == null || costs.Count == 0) yield return L.T("MT_ConfigParagonCosts");
        else foreach (var cost in costs)
            if (cost?.thingDef == null || cost.count <= 0) yield return L.T("MT_ConfigParagonCostItem");
    }
    private static bool Valid(float value, float maximum) => float.IsFinite(value) && value >= 1f && value <= maximum;
    private static bool UnitFactor(float value) => float.IsFinite(value) && value > 0f && value <= 1f;
}
