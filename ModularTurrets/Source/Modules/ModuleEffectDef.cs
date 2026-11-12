using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace ModularTurrets;

// Content packs can compose effects in XML or supply a worker subclass in their DLL.
public sealed class ModuleEffectDef : Def
{
    public Type workerClass = typeof(ModuleEffectWorker);
    public bool affectsAuxiliary;
    public bool preventFriendlyFire;
    public int branches = 3;
    public float angle = 12f;
    public float anglePerTier = -1f;
    public float damageFactor = 0.34f;
    public float damagePerTier = 0.042f;
    public float rangeFactorPerPower, rangeFlatPerPower, damageFactorPerPower, armorPerPower;
    public float aimingReductionPerPower, coolingReductionPerPower, shieldPerPower, turnSpeedIncreasePerPower, accuracyMissReductionPerPower;
    public float mortarScatterReductionBase, mortarScatterReductionPerTier;
    private ModuleEffectWorker worker;
    // One worker is shared by this def. Put per-shot state in the context below.
    public ModuleEffectWorker Worker => worker ??= (ModuleEffectWorker)Activator.CreateInstance(workerClass);
    public override IEnumerable<string> ConfigErrors()
    {
        foreach (var error in base.ConfigErrors()) yield return error;
        if (workerClass == null || workerClass.IsAbstract || !typeof(ModuleEffectWorker).IsAssignableFrom(workerClass)) yield return L.T("MT_ConfigWorkerType");
        if (branches < 1 || branches > 9) yield return L.T("MT_ConfigBranches");
        if (damageFactor < 0f || damageFactor + damagePerTier * 5f < 0f) yield return L.T("MT_ConfigDamage");
        if (!float.IsFinite(accuracyMissReductionPerPower) || accuracyMissReductionPerPower < 0f) yield return L.T("MT_ConfigAccuracy");
        if (!float.IsFinite(mortarScatterReductionBase) || mortarScatterReductionBase < 0f
            || !float.IsFinite(mortarScatterReductionPerTier) || mortarScatterReductionPerTier < 0f
            || mortarScatterReductionBase + 5f * mortarScatterReductionPerTier >= 1f)
            yield return L.T("MT_ContextScatterConfig");
    }
}
