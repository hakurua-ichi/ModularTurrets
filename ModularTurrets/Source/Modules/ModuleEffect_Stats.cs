using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace ModularTurrets;

public sealed class ModuleEffect_Stats : ModuleEffectWorker
{
    internal static float MortarScatterFactor(ModuleEffectDef effect, int tier)
    {
        float reduction = effect.mortarScatterReductionBase
            + effect.mortarScatterReductionPerTier * Math.Clamp(tier, 0, 5);
        return float.IsFinite(reduction) ? 1f - Math.Clamp(reduction, 0f, 0.95f) : 1f;
    }

    public override void ModifyStats(ModuleEffectContext c, ModuleModifiers m)
    {
        var d = c.Definition; float p = c.Power;
        m.RangeFactor *= Math.Max(0.01f, 1f + d.rangeFactorPerPower * p); m.RangeFlat += d.rangeFlatPerPower * p;
        m.DamageFactor *= Math.Max(0f, 1f + d.damageFactorPerPower * p); m.ArmorBonus += d.armorPerPower * p;
        m.AimingFactor /= Math.Max(1f, 1f + d.aimingReductionPerPower * p);
        m.CoolingFactor /= Math.Max(1f, 1f + d.coolingReductionPerPower * p); m.ShieldBonus += d.shieldPerPower * p;
        m.TurnSpeedFactor *= Math.Max(1f, 1f + d.turnSpeedIncreasePerPower * p);
        m.AccuracyMissFactor /= Math.Max(1f, 1f + d.accuracyMissReductionPerPower * p);
        if (d.mortarScatterReductionBase > 0f || d.mortarScatterReductionPerTier > 0f)
            m.MortarScatterFactor *= MortarScatterFactor(d, c.Tier);
        m.PreventFriendlyFire |= d.preventFriendlyFire;
    }
}
