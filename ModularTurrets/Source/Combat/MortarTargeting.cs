using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace ModularTurrets;

internal static class MortarTargeting
{
    // Read the resolved Core definition after optional mods patch it. CE's
    // mortar remains authoritative without a CE dependency in this assembly.
    internal static VerbProperties VanillaProperties()
    {
        var gun = DefDatabase<ThingDef>.GetNamedSilentFail("Turret_Mortar")?.building?.turretGunDef
            ?? DefDatabase<ThingDef>.GetNamedSilentFail("Artillery_Mortar")
            ?? DefDatabase<ThingDef>.GetNamedSilentFail("Gun_Mortar");
        var verbs = gun?.Verbs;
        return verbs?.FirstOrDefault(v => v.isPrimary) ?? verbs?.FirstOrDefault();
    }
    internal static bool SourceRoofed(Thing caster)
        => caster?.Map == null || caster.Map.roofGrid.Roofed(caster.Position);
    internal static bool CanFireAt(Thing caster, IntVec3 root, LocalTargetInfo target)
        => caster?.Spawned == true && target.IsValid && target.Cell.InBounds(caster.Map)
            && !SourceRoofed(caster);
}
