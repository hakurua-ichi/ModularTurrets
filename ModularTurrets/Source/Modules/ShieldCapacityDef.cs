using System;
using System.Collections.Generic;
using Verse;

namespace ModularTurrets;

// Capacity is separate from generic module power so its final tier stays explicit.
public sealed class ShieldCapacityDef : Def
{
    public ThingDef module;
    public List<float> tierCapacities = new();
    public float paragonCapacity;

    public override IEnumerable<string> ConfigErrors()
    {
        foreach (string error in base.ConfigErrors()) yield return error;
        if (module?.GetModExtension<PartExtension>()?.moduleKind != "Shield")
            yield return L.T("MT_ConfigShieldModule");
        if (tierCapacities == null || tierCapacities.Count != 6)
            yield return L.T("MT_ConfigShieldTiers");
        else
        {
            float previous = 0f;
            foreach (float capacity in tierCapacities)
            {
                if (!float.IsFinite(capacity) || capacity <= 0f || capacity < previous)
                    yield return L.T("MT_ConfigShieldCapacity");
                previous = capacity;
            }
            if (!float.IsFinite(paragonCapacity) || paragonCapacity < previous)
                yield return L.T("MT_ConfigParagonShield");
        }
    }
}
