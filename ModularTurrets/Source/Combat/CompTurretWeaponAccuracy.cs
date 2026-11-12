using RimWorld;
using UnityEngine;
using Verse;

namespace ModularTurrets;

// Core's ShotReport combines per-cell turret accuracy and four weapon stats.
// Keep both adjustments on their own instances; shared Defs remain unchanged.
public sealed class CompTurretWeaponAccuracy : ThingComp
{
    public float MissFactor = 1f;

    public override float GetStatOffset(StatDef stat)
    {
        if (ModularMarketValue.AppliesTo(stat)) return ModularMarketValue.StatOffset(parent, stat);
        if (stat != StatDefOf.AccuracyTouch && stat != StatDefOf.AccuracyShort
            && stat != StatDefOf.AccuracyMedium && stat != StatDefOf.AccuracyLong
            && stat != StatDefOf.ShootingAccuracyTurret) return 0f;
        float baseline = parent.def.statBases.GetStatValueFromList(stat, stat.defaultBaseValue);
        return (1f - Mathf.Clamp01(baseline)) * (1f - Mathf.Clamp01(MissFactor));
    }

    public override void GetStatsExplanation(StatDef stat, System.Text.StringBuilder text, string whitespace = "")
    {
        base.GetStatsExplanation(stat, text, whitespace);
        ModularMarketValue.Explain(parent, stat, text, whitespace);
    }

    public override void PostExposeData()
    {
        base.PostExposeData();
        Scribe_Values.Look(ref MissFactor, "modularAccuracyMissFactor", 1f);
    }
}
