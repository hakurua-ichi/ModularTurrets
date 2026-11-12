using System;
using System.Collections.Generic;
using Verse;

namespace ModularTurrets;

public static class ModuleUpgradeCosts
{
    public static List<ThingDefCountClass> ForUpgrade(ThingDef part, int tier, bool paragon = false)
    {
        var costs = OriginalCosts(part, tier, paragon);
        // Swap new ordinary upgrade quotes only. Old receipts and queued orders
        // keep their original materials and the legacy estimate remains separate.
        if (!paragon)
            foreach (var cost in costs)
                if (cost.thingDef.defName == "Steel") cost.thingDef = DefDatabase<ThingDef>.GetNamed("Plasteel");
                else if (cost.thingDef.defName == "Plasteel") cost.thingDef = DefDatabase<ThingDef>.GetNamed("Steel");
        if (part?.GetModExtension<PartExtension>()?.isHead == false)
        {
            var settings = DefDatabase<ModuleUpgradeCostDef>.GetNamedSilentFail("MT_ModuleUpgradeCosts");
            int multiplier = paragon ? settings?.paragonMultiplier ?? 1
                : tier >= 1 && tier <= 5 && settings?.tierMultipliers?.Count == 5 ? settings.tierMultipliers[tier - 1] : 1;
            multiplier = Math.Clamp(multiplier, 1, 4);
            foreach (var cost in costs) cost.count = (int)Math.Min(int.MaxValue, (long)cost.count * multiplier);
            if (paragon && settings is { } pricing && float.IsFinite(pricing.paragonMaterialFactor)
                && pricing.paragonMaterialFactor > 0f && pricing.paragonMaterialFactor < 1f)
                foreach (var cost in costs)
                    cost.count = Math.Max(1, (int)Math.Round(cost.count * (double)pricing.paragonMaterialFactor,
                        MidpointRounding.AwayFromZero));
        }
        // Paragon XML already contains its new component quantities.
        if (!paragon) ComponentEconomy.ApplyToNewQuote(costs);
        return costs;
    }

    // Old saves lack receipts. Their estimate must use the old prices, never the new multiplier.
    internal static List<ThingDefCountClass> OriginalCosts(ThingDef part, int tier, bool paragon = false)
    {
        var result = new List<ThingDefCountClass>();
        if (paragon)
        {
            foreach (var cost in ParagonUtility.Settings(part)?.costs ?? new List<ThingDefCountClass>())
                if (cost?.thingDef != null && cost.count > 0) result.Add(new ThingDefCountClass(cost.thingDef, cost.count));
            return result;
        }
        if (tier < 1 || tier > 5) return result;
        string[] names = { "Steel", "Plasteel", "Uranium", "ComponentSpacer" };
        int[] counts = { 60 * tier, 30 * tier * tier * tier, 10 * tier * tier * tier, tier * tier };
        for (int i = 0; i < names.Length; i++) result.Add(new ThingDefCountClass(DefDatabase<ThingDef>.GetNamed(names[i]), counts[i]));
        return result;
    }
}
