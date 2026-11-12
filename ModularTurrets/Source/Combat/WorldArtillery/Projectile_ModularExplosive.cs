using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace ModularTurrets;

public partial class Projectile_ModularExplosive
{
    internal void ResumeOffMap(Vector3 entry, IntVec3 used, IntVec3 intended)
    {
        // Reuse the already-saved damage/IFF/opening/extra-damage snapshot.
        // Calling this class's Launch again would reread the source turret.
        ThingDef originalEquipmentDef = equipmentDef;
        float originalStoppingPower = stoppingPower;
        base.Launch(launcher, entry, used, intended, HitFlags, preventFriendlyFire, equipment: null);
        equipmentDef = originalEquipmentDef;
        stoppingPower = originalStoppingPower;
    }
}
