using System;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace ModularTurrets;

public sealed partial class CompTurretPart
{
    private bool upgradeInvestmentInitialized, upgradeInvestmentEstimated;
    private List<UpgradeInvestmentEntry> upgradeInvestment = new();
    internal IReadOnlyList<UpgradeInvestmentEntry> UpgradeInvestmentEntries
    {
        get { EnsureUpgradeInvestment(); return upgradeInvestment; }
    }
    internal bool UpgradeInvestmentEstimated
    {
        get { EnsureUpgradeInvestment(); return upgradeInvestmentEstimated; }
    }
    private void EnsureUpgradeInvestment()
    {
        upgradeInvestment ??= new();
        if (upgradeInvestmentInitialized) return;
        upgradeInvestmentInitialized = true;
        if (!Building_ModularTurret.CanUpgradePart(parent?.def)) return;
        int tier = Math.Clamp(Tier, 0, 5);
        upgradeInvestmentEstimated = tier > 0 || IsParagon;
        for (int next = 1; next <= tier; next++)
            UpgradeInvestment.Add(upgradeInvestment, ModuleUpgradeCosts.OriginalCosts(parent.def, next));
        if (IsParagon && tier == 5)
            UpgradeInvestment.Add(upgradeInvestment, ModuleUpgradeCosts.OriginalCosts(parent.def, 5, true));
    }
    internal void RecordUpgradeInvestment(IEnumerable<ThingDefCountClass> paidCosts)
    {
        EnsureUpgradeInvestment();
        UpgradeInvestment.Add(upgradeInvestment, paidCosts);
    }
    public override void PostSplitOff(Thing piece)
    {
        base.PostSplitOff(piece);
        if (piece == parent || piece.TryGetComp<CompTurretPart>() is not { } split) return;
        // Each physical item owns its history. A shared ledger would let an
        // upgrade on the new item silently change the original item's value.
        EnsureUpgradeInvestment();
        split.Tier = Tier;
        split.IsParagon = IsParagon;
        split.EmergencyShieldReadyTick = EmergencyShieldReadyTick;
        split.upgradeInvestment = new();
        UpgradeInvestment.Add(split.upgradeInvestment, upgradeInvestment);
        split.upgradeInvestmentInitialized = upgradeInvestmentInitialized;
        split.upgradeInvestmentEstimated = upgradeInvestmentEstimated;
    }
    private void ExposeUpgradeInvestment()
    {
        if (Scribe.mode == LoadSaveMode.Saving) EnsureUpgradeInvestment();
        Scribe_Values.Look(ref upgradeInvestmentInitialized, "upgradeInvestmentInitialized", false);
        Scribe_Values.Look(ref upgradeInvestmentEstimated, "upgradeInvestmentEstimated", false);
        Scribe_Collections.Look(ref upgradeInvestment, "upgradeInvestment", LookMode.Deep);
        if (Scribe.mode == LoadSaveMode.PostLoadInit)
        {
            upgradeInvestment ??= new();
            UpgradeInvestment.Clean(upgradeInvestment);
        }
    }
}
