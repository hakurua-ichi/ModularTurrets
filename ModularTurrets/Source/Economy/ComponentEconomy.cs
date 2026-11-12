using System;
using System.Collections.Generic;
using Verse;

namespace ModularTurrets;

// Apply once to a newly calculated quote, after all quantity multipliers.
internal static class ComponentEconomy
{
    internal static void ApplyToNewQuote(List<ThingDefCountClass> costs)
    {
        long ordinary = 0;
        for (int i = costs.Count - 1; i >= 0; i--)
        {
            var cost = costs[i];
            if (cost.thingDef.defName != "ComponentSpacer" || cost.count <= 0) continue;
            int retained = (int)Math.Round(cost.count * 0.4d, MidpointRounding.AwayFromZero);
            ordinary += (long)(cost.count - retained) * 3;
            if (retained == 0) costs.RemoveAt(i);
            else cost.count = retained;
        }
        if (ordinary == 0) return;
        foreach (var cost in costs)
            if (cost.thingDef.defName == "ComponentIndustrial")
            {
                cost.count = (int)Math.Min(int.MaxValue, cost.count + ordinary);
                return;
            }
        costs.Add(new ThingDefCountClass(DefDatabase<ThingDef>.GetNamed("ComponentIndustrial"),
            (int)Math.Min(int.MaxValue, ordinary)));
    }
}
