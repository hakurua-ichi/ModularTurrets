using System;
using System.Collections.Generic;
using System.Text;
using ModularTurrets.Batteries;
using RimWorld;
using Verse;

namespace ModularTurrets;

internal static class ModularMarketValue
{
    internal static float InstalledFraction => InstalledPartWealth.TransferEnabled ? 1f : 0f;
    internal const float UpgradeFraction = 0.05f;

    internal static bool AppliesTo(StatDef stat)
        => stat == StatDefOf.MarketValue || stat == StatDefOf.MarketValueIgnoreHp;

    internal static float StatOffset(Thing thing, StatDef stat)
    {
        if (!AppliesTo(stat)) return 0f;
        float bonus = Bonus(thing);
        // Core's fixed-price worker visits comp offsets twice. Calculated prices
        // visit once; IgnoreHp delegates fixed prices to the MarketValue worker.
        return thing.def.statBases.StatListContains(StatDefOf.MarketValue) ? bonus * 0.5f : bonus;
    }

    private static float Bonus(Thing thing)
    {
        if (thing is Building_ModularTurret turret) return turret.InstalledMarketValueBonus();
        if (thing is Building_ModularBattery battery) return battery.InstalledMarketValueBonus();
        if (thing.TryGetComp<CompTurretPart>() is { } part)
            return Amount(InvestmentValue(part.UpgradeInvestmentEntries) * UpgradeFraction);
        if (thing.TryGetComp<CompBatteryModuleUpgrade>() is { } module)
        {
            double level = module.Level;
            var settings = module.Settings;
            return settings == null ? 0f : Amount(level * (level + 1d) * 0.5d * UpgradeFraction
                * ResourceValue(settings.upgradeSteelPerLevel, settings.upgradeComponentsPerLevel));
        }
        return 0f;
    }

    internal static double InvestmentValue(IReadOnlyList<UpgradeInvestmentEntry> ledger)
    {
        double value = 0d;
        for (int i = 0; i < ledger.Count; i++)
            if (ledger[i] is { Resource: { } resource, Count: > 0 } entry)
                value += entry.Count * (double)resource.BaseMarketValue;
        return value;
    }

    internal static double ResourceValue(int steel, int components)
        => Math.Max(0, steel) * (double)ThingDefOf.Steel.BaseMarketValue
            + Math.Max(0, components) * (double)ThingDefOf.ComponentIndustrial.BaseMarketValue;

    internal static float Amount(double value)
        => double.IsFinite(value) ? (float)Math.Clamp(value, 0d, float.MaxValue / 16d) : 0f;

    internal static void Explain(Thing thing, StatDef stat, StringBuilder text, string whitespace)
    {
        if (!AppliesTo(stat)) return;
        float bonus = Bonus(thing);
        if (bonus > 0f) text.AppendLine(whitespace + L.T("MT_MarketValueBonus", bonus.ToString("0.##")));
    }

    internal static void Invalidate(Thing thing)
    {
        StatDefOf.MarketValue.Worker.ClearCacheForThing(thing);
        StatDefOf.MarketValueIgnoreHp.Worker.ClearCacheForThing(thing);
    }
}
