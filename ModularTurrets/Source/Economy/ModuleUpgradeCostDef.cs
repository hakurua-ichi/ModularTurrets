using System;
using System.Collections.Generic;
using Verse;

namespace ModularTurrets;

public sealed class ModuleUpgradeCostDef : Def
{
    public List<int> tierMultipliers = new();
    public int paragonMultiplier = 1;
    public float paragonMaterialFactor = 1f;
    public override IEnumerable<string> ConfigErrors()
    {
        foreach (string error in base.ConfigErrors()) yield return error;
        if (tierMultipliers == null || tierMultipliers.Count != 5)
            yield return L.T("MT_ConfigUpgradeCostTiers");
        else foreach (int multiplier in tierMultipliers)
            if (multiplier < 1 || multiplier > 4) yield return L.T("MT_ConfigUpgradeCostBounds");
        if (paragonMultiplier < 1 || paragonMultiplier > 4)
            yield return L.T("MT_ConfigParagonCostBounds");
        if (!float.IsFinite(paragonMaterialFactor) || paragonMaterialFactor <= 0f || paragonMaterialFactor > 1f)
            yield return L.T("MT_ConfigParagonMaterialFactor");
    }
}
