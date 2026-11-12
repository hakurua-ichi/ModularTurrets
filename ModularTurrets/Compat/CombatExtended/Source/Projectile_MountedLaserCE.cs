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

public sealed class Projectile_MountedLaserCE : LaserBeamCE
{
    public override void SpawnBeam(Vector3 from,Vector3 to)
    {
        if(launcher is not Building_ModularTurret owner)throw new InvalidOperationException("MT CE laser requires its mounted owner.");
        // Physical CE y is height; the original beam uses map render altitude.
        Vector3 start=owner.MuzzleFor(equipment);to.y=start.y;
        owner.ShowBeam(start,to,equipmentDef?.GetModExtension<BeamVisualExtension>());SpecialWeapons.LaserBeams++;
    }
    public override void RayCast(Thing launcher,VerbProperties verbProps,Vector2 origin,float shotAngle,float shotRotation,
        float shotHeight=0f,float shotSpeed=-1f,float spreadDegrees=0f,float aperatureSize=0.03f,Thing equipment=null,bool useSameHeight=false)
    {
        using var effects=new SpecialWeapons.ImpactScope(this);
        // The common prefix captures this base RayCast exactly once.
        try{base.RayCast(launcher,verbProps,origin,shotAngle,shotRotation,shotHeight,shotSpeed,spreadDegrees,aperatureSize,equipment,useSameHeight);}
        finally{if(Spawned&&!Destroyed)Destroy();} // A protected CE impact can be skipped before its normal cleanup.
    }
}
