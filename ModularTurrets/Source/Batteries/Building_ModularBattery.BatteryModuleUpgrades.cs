using System;
using RimWorld;
using Verse;

namespace ModularTurrets.Batteries;

public sealed partial class Building_ModularBattery
{
    internal float InstalledMarketValueBonus()
    {
        double bonus = Math.Max(0, CapacityLevel) * ModularMarketValue.UpgradeFraction
            * ModularMarketValue.ResourceValue(Settings?.upgradeSteel > 0 ? Settings.upgradeSteel : 80,
                Settings?.upgradeComponents > 0 ? Settings.upgradeComponents : 2);
        if (leftModule is { Destroyed: false }) bonus += leftModule.MarketValue * ModularMarketValue.InstalledFraction;
        if (rightModule is { Destroyed: false } && rightModule != leftModule)
            bonus += rightModule.MarketValue * ModularMarketValue.InstalledFraction;
        return ModularMarketValue.Amount(bonus);
    }

    private void RefreshMarketValue()
    {
        if (leftModule != null) ModularMarketValue.Invalidate(leftModule);
        if (rightModule != null) ModularMarketValue.Invalidate(rightModule);
        ModularMarketValue.Invalidate(this);
    }

    private CompBatteryModuleUpgrade FittedUpgrade(string slot)
    {
        var part = ModuleAt(slot);
        return FittedExtension(part) == null ? null : part.TryGetComp<CompBatteryModuleUpgrade>();
    }

    public int ModuleUpgradeLevel(string slot) => FittedUpgrade(slot)?.Level ?? 0;
    public int ModuleUpgradeMaxLevel(string slot) => FittedUpgrade(slot)?.MaxLevel ?? 0;

    public bool TryGetModuleUpgradeQuote(string slot, out int targetLevel, out int steel,
        out int components, out float work)
    {
        targetLevel = steel = components = 0;
        work = 0f;
        var upgrade = FittedUpgrade(slot);
        if (upgrade == null || upgrade.Level >= upgrade.MaxLevel) return false;
        int next = upgrade.Level + 1;
        var settings = upgrade.Settings;
        long steelCost = (long)settings.upgradeSteelPerLevel * next;
        long componentCost = (long)settings.upgradeComponentsPerLevel * next;
        float workCost = settings.upgradeWorkPerLevel * next;
        if (steelCost is <= 0 or > int.MaxValue || componentCost is <= 0 or > int.MaxValue
            || !float.IsFinite(workCost) || workCost <= 0f) return false;
        targetLevel = next;
        steel = (int)steelCost;
        components = (int)componentCost;
        work = workCost;
        return true;
    }

    public bool CanQueueModuleUpgrade(string slot) => CanQueue && !HasSlotOrder(slot)
        && TryGetModuleUpgradeQuote(slot, out _, out _, out _, out _);

    public bool QueueModuleUpgrade(string slot)
    {
        if (!CanQueue || HasSlotOrder(slot)
            || !TryGetModuleUpgradeQuote(slot, out int next, out int steelCount, out int componentCount, out float work))
            return false;
        var steel = DefDatabase<ThingDef>.GetNamedSilentFail("Steel");
        var components = DefDatabase<ThingDef>.GetNamedSilentFail("ComponentIndustrial");
        if (steel == null || components == null) return false;
        var part = ModuleAt(slot);
        return AddOrder(new BatteryOrder
        {
            Kind = BatteryOperation.UpgradeModule, Slot = slot, PartDef = part.def, PartId = part.thingIDNumber,
            TargetLevel = next, WorkTotal = work,
            Costs = new() { new ThingDefCountClass(steel, steelCount), new ThingDefCountClass(components, componentCount) }
        });
    }

    private CompBatteryModuleUpgrade OrderedModule(BatteryOrder order)
    {
        var part = ModuleAt(order.Slot);
        return part != null && part.thingIDNumber == order.PartId && part.def == order.PartDef
            ? FittedUpgrade(order.Slot) : null;
    }
}
