using System;
using System.Collections.Generic;
using Verse;

namespace ModularTurrets;

public sealed partial class Building_ModularTurret
{
    private float InstalledShieldCapacity
    {
        get
        {
            float largest = 0f, total = 0f;
            foreach (var mount in fitted)
            {
                if (mount.Part?.def.GetModExtension<PartExtension>()?.moduleKind != "Shield") continue;
                float capacity = ShieldCapacityUtility.Capacity(mount.Part);
                largest = Math.Max(largest, capacity);
                total += capacity;
            }
            // Preserve strongest 100% plus each additional module at 35%.
            return largest + 0.35f * (total - largest);
        }
    }
}
