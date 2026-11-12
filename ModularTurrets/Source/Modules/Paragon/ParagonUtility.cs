using System;
using System.Collections.Generic;
using Verse;

namespace ModularTurrets;

public static class ParagonUtility
{
    public static ParagonModuleExtension Settings(ThingDef def)
        => def?.GetModExtension<ParagonModuleExtension>();
    public static bool IsEligible(ThingDef def)
        => def?.GetModExtension<PartExtension>() is { isHead: false }
            && def.GetModExtension<SpecialistModuleExtension>() == null && Settings(def) != null;
    public static bool IsParagon(Thing part)
        => part?.TryGetComp<CompTurretPart>() is { Tier: 5, IsParagon: true } && IsEligible(part.def);
    public static float Factor(float value, float maximum)
        => float.IsFinite(value) ? Math.Clamp(value, 1f, maximum) : 1f;
    public static float Power(Thing part) => Power(part, part?.TryGetComp<CompTurretPart>()?.Tier ?? 0, IsParagon(part) && ParagonMapRegistry.IsEffective(part));
    public static float Power(Thing part, int tier, bool paragon)
        => (1 << Math.Clamp(tier, 0, 5)) * (paragon && tier == 5 && IsEligible(part?.def)
            ? Factor(Settings(part.def).powerMultiplier, 8f) : 1f);
    public static float PowerConsumption(Thing part)
        => PowerConsumption(part, part?.TryGetComp<CompTurretPart>()?.Tier ?? 0, IsParagon(part) && ParagonMapRegistry.IsEffective(part));
    public static float PowerConsumption(Thing part, int tier, bool paragon)
        => (part?.def.GetModExtension<PartExtension>()?.power ?? 0f) * (1f + 0.2f * Math.Clamp(tier, 0, 5))
            * (paragon && tier == 5 && IsEligible(part?.def) ? Factor(Settings(part.def).powerConsumptionMultiplier, 4f) : 1f);
}
