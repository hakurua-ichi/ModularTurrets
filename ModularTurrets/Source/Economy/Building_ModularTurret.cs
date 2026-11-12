using System;
using System.Collections.Generic;
using System.Text;
using ModularTurrets.Batteries;
using RimWorld;
using Verse;

namespace ModularTurrets;

public sealed partial class Building_ModularTurret
{
    internal float InstalledMarketValueBonus()
    {
        EnsureBaseUpgradeInvestment();
        double bonus = ModularMarketValue.InvestmentValue(baseUpgradeInvestment) * ModularMarketValue.UpgradeFraction;
        for (int i = 0; i < fitted.Count; i++)
        {
            Thing part = fitted[i]?.Part;
            if (part == null || part.Destroyed || part == this) continue;
            bool duplicate = false;
            for (int j = 0; j < i; j++)
                if (fitted[j]?.Part == part) { duplicate = true; break; }
            if (!duplicate) bonus += part.MarketValue * ModularMarketValue.InstalledFraction;
        }
        return ModularMarketValue.Amount(bonus);
    }

    internal bool IsFittedWealthPart(Thing part)
    {
        foreach (var mount in fitted)
            if (mount?.Part == part) return true;
        return false;
    }

    private void RefreshMarketValue()
    {
        foreach (var mount in fitted)
            if (mount?.Part is { } part) ModularMarketValue.Invalidate(part);
        ModularMarketValue.Invalidate(this);
    }
}
