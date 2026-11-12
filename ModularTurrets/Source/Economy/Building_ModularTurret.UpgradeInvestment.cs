using System;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace ModularTurrets;

public sealed partial class Building_ModularTurret
{
    private bool baseUpgradeInvestmentInitialized, baseUpgradeInvestmentEstimated;
    private List<UpgradeInvestmentEntry> baseUpgradeInvestment = new();

    private void EnsureBaseUpgradeInvestment()
    {
        baseUpgradeInvestment ??= new();
        if (baseUpgradeInvestmentInitialized) return;
        baseUpgradeInvestmentInitialized = true;
        foreach (int completed in new[] { DamageLevel, ArmorLevel, RangeLevel })
        {
            if (completed <= 0) continue;
            baseUpgradeInvestmentEstimated = true;
            UpgradeInvestment.Add(baseUpgradeInvestment, DefDatabase<ThingDef>.GetNamed("Plasteel"), LegacyBaseResourceTotal(completed, 60, 8f));
            UpgradeInvestment.Add(baseUpgradeInvestment, DefDatabase<ThingDef>.GetNamed("Uranium"), LegacyBaseResourceTotal(completed, 30, 4f));
            UpgradeInvestment.Add(baseUpgradeInvestment, DefDatabase<ThingDef>.GetNamed("ComponentSpacer"), LegacyBaseResourceTotal(completed, 3, .25f));
        }
    }

    // Group equal prices, preserving the old single-precision square-root rule.
    // This avoids iterating billions of levels if a legacy save contains a huge value.
    private static long LegacyBaseResourceTotal(int levels, int initial, float coefficient)
    {
        long total = 0;
        for (int start = 0; start < levels;)
        {
            int price = initial + (int)(coefficient * MathF.Sqrt(start));
            int low = start + 1, high = levels;
            while (low < high)
            {
                int middle = low + (high - low) / 2;
                if (initial + (int)(coefficient * MathF.Sqrt(middle)) == price) low = middle + 1;
                else high = middle;
            }
            total += (long)(low - start) * price;
            start = low;
        }
        return total;
    }

    private void RecordCompletedUpgradeInvestment(TurretOrder order)
    {
        // Called before changing the tier/level, so legacy estimates exclude this purchase.
        if (order.Kind == TurretOperation.UpgradeModule)
            PartAt(order.Slot)?.TryGetComp<CompTurretPart>()?.RecordUpgradeInvestment(order.Costs);
        else if (IsBaseUpgrade(order.Kind))
        {
            EnsureBaseUpgradeInvestment();
            UpgradeInvestment.Add(baseUpgradeInvestment, order.Costs);
        }
    }

    public List<ThingDefCountClass> GetRestorationCosts()
    {
        EnsureBaseUpgradeInvestment();
        var total = new List<UpgradeInvestmentEntry>();
        UpgradeInvestment.Add(total, baseUpgradeInvestment);
        var visited = new HashSet<Thing>();
        foreach (var mount in fitted)
        {
            var part = mount?.Part;
            if (part == null || !visited.Add(part)) continue;
            UpgradeInvestment.Add(total, part.def.CostList);
            if (part.TryGetComp<CompTurretPart>() is { } comp)
                UpgradeInvestment.Add(total, comp.UpgradeInvestmentEntries);
        }
        // Existing order/hauling counts use Int32. Keep accumulated receipts in Int64.
        decimal fraction = 1m - (decimal)RestorationDiscount;
        return total.Where(e => e.Count > 0).OrderBy(e => e.Resource.defName)
            .Select(e => new ThingDefCountClass(e.Resource, (int)Math.Min(int.MaxValue,
                Math.Ceiling((e.Count / 2 + e.Count % 2) * fraction)))).ToList();
    }

    public float RestorationDiscount
        => float.IsFinite(Head?.restorationDiscount ?? 0f)
            ? Math.Clamp(Head?.restorationDiscount ?? 0f, 0f, 0.75f) : 0f;

    public bool RestorationCostsEstimated
    {
        get
        {
            EnsureBaseUpgradeInvestment();
            return baseUpgradeInvestmentEstimated || fitted.Any(m => m.Part?.TryGetComp<CompTurretPart>()?.UpgradeInvestmentEstimated == true);
        }
    }

    private void ExposeUpgradeInvestment()
    {
        if (Scribe.mode == LoadSaveMode.Saving) EnsureBaseUpgradeInvestment();
        Scribe_Values.Look(ref baseUpgradeInvestmentInitialized, "baseUpgradeInvestmentInitialized", false);
        Scribe_Values.Look(ref baseUpgradeInvestmentEstimated, "baseUpgradeInvestmentEstimated", false);
        Scribe_Collections.Look(ref baseUpgradeInvestment, "baseUpgradeInvestment", LookMode.Deep);
        if (Scribe.mode == LoadSaveMode.PostLoadInit)
        {
            baseUpgradeInvestment ??= new();
            UpgradeInvestment.Clean(baseUpgradeInvestment);
        }
    }
}
