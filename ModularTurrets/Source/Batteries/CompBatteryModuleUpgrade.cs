using System;
using RimWorld;
using Verse;

namespace ModularTurrets.Batteries;

public sealed class CompBatteryModuleUpgrade : ThingComp
{
    public override float GetStatOffset(StatDef stat)
        => base.GetStatOffset(stat) + ModularMarketValue.StatOffset(parent, stat);

    public override void GetStatsExplanation(StatDef stat, System.Text.StringBuilder text, string whitespace = "")
    {
        base.GetStatsExplanation(stat, text, whitespace);
        ModularMarketValue.Explain(parent, stat, text, whitespace);
    }

    private int level;
    internal BatteryModuleExtension Settings => parent.def.GetModExtension<BatteryModuleExtension>();
    public int MaxLevel => Settings?.MaximumLevel ?? 0;
    public int Level => Math.Clamp(level, 0, MaxLevel);

    public override string TransformLabel(string label)
        => Level > 0 ? L.T("MT_BatteryModuleItemLabel", label, Level) : label;

    public override string CompInspectStringExtra() => L.T("MT_BatteryModuleItemLevel", Level, MaxLevel);

    // Defs use stacks of one. Keep grades separate if another mod raises that limit.
    public override bool AllowStackWith(Thing other) => other.TryGetComp<CompBatteryModuleUpgrade>()?.Level == Level
        && !WorkGiver_ModularBatteryAssembly.PartClaimedByOther(parent.MapHeld, parent.thingIDNumber)
        && !WorkGiver_ModularBatteryAssembly.PartClaimedByOther(other.MapHeld, other.thingIDNumber);

    public override void PostSplitOff(Thing piece)
    {
        base.PostSplitOff(piece);
        if (piece.TryGetComp<CompBatteryModuleUpgrade>() is { } split) split.level = Level;
    }

    public override void PostExposeData()
    {
        base.PostExposeData();
        Scribe_Values.Look(ref level, "batteryModuleUpgradeLevel", 0);
        if (Scribe.mode == LoadSaveMode.PostLoadInit) level = Level;
    }

    internal bool CanAdvanceTo(int target) => target > 0 && target <= MaxLevel && Level == target - 1;

    internal bool TryAdvanceTo(int target)
    {
        if (!CanAdvanceTo(target)) return false;
        level = target;
        return true;
    }
}
