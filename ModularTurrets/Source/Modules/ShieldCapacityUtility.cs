using System;
using System.Collections.Generic;
using Verse;

namespace ModularTurrets;

public static class ShieldCapacityUtility
{
    public static float Capacity(Thing part)
        => Capacity(part, part?.TryGetComp<CompTurretPart>()?.Tier ?? 0,
            ParagonUtility.IsParagon(part) && ParagonMapRegistry.IsEffective(part));

    public static float Capacity(Thing part, int tier, bool paragon)
    {
        tier = Math.Clamp(tier, 0, 5);
        foreach (var settings in DefDatabase<ShieldCapacityDef>.AllDefsListForReading)
        {
            if (settings.module != part?.def || settings.tierCapacities?.Count != 6) continue;
            float capacity = paragon && tier == 5 && ParagonUtility.IsEligible(part?.def)
                ? settings.paragonCapacity : settings.tierCapacities[tier];
            if (float.IsFinite(capacity) && capacity > 0f) return capacity;
        }
        // Third-party shield modules without an explicit table retain their rule.
        return 40f * ParagonUtility.Power(part, tier, paragon);
    }
}
