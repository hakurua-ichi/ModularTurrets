using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CombatExtended;
using CombatExtended.Lasers;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace ModularTurrets.Compat.CombatExtended;

public sealed class Verb_MountedTeslaCE : Verb_MountedPhaseCE
{
    private Func<bool> muzzle,opening;
    protected override bool ShouldAim=>false;
    // The original Tesla pulse never used per-target BuildShots branches.
    // One CE shot/reload cost emits one field, irrespective of collateral count.
    public override bool TryCastShot()=>caster is Building_ModularTurret owner
        ?owner.WithShotMuzzle(this,EquipmentSource,muzzle??=FromMuzzle):throw new InvalidOperationException("MT CE Tesla requires its mounted owner.");
    private bool FromMuzzle()=>((Building_ModularTurret)caster).WithParagonOpeningShot(EquipmentSource,opening??=CastSingle);
}
