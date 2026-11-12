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

public sealed class Projectile_MountedPulseHitCE : Projectile_MountedFieldCE
{
    internal void Apply(ProjectileCE parent,Thing hit)
    {
        intendedTarget=hit;
        Prepare(parent.launcher,parent.equipment,parent.origin,parent.shotAngle,parent.shotRotation,parent.shotHeight,parent.shotSpeed);
        // A field pulse has no travel/scatter or intervening obstruction. Use CE's
        // physical target bounds for its injury body region, never render altitude.
        var bounds=CE_Utility.GetBoundsFor(hit);
        ExactPosition=LastPos=bounds.center;Destination=new Vector2(ExactPosition.x,ExactPosition.z);
        Impact(hit);
    }
}
