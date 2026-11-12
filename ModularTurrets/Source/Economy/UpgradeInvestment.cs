using System;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace ModularTurrets;

internal static class UpgradeInvestment
{
    public static void Add(List<UpgradeInvestmentEntry> ledger, ThingDef resource, long count)
    {
        if (resource == null || count <= 0) return;
        var entry = ledger.FirstOrDefault(e => e.Resource == resource);
        if (entry == null) ledger.Add(new UpgradeInvestmentEntry { Resource = resource, Count = count });
        else entry.Count = entry.Count > long.MaxValue - count ? long.MaxValue : entry.Count + count;
    }
    public static void Add(List<UpgradeInvestmentEntry> ledger, IEnumerable<ThingDefCountClass> costs)
    {
        foreach (var cost in costs ?? Enumerable.Empty<ThingDefCountClass>())
            if (cost != null) Add(ledger, cost.thingDef, cost.count);
    }
    public static void Add(List<UpgradeInvestmentEntry> ledger, IEnumerable<UpgradeInvestmentEntry> source)
    {
        foreach (var entry in source) if (entry != null) Add(ledger, entry.Resource, entry.Count);
    }
    public static void Clean(List<UpgradeInvestmentEntry> ledger)
        => ledger.RemoveAll(entry => entry?.Resource == null || entry.Count <= 0);
}
