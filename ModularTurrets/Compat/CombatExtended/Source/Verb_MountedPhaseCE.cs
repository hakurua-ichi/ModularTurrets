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

// Energy fields retain their original obstacle-independent behavior. Range,
// aiming, ammunition and the actual injury/armor work still belong to CE.
public class Verb_MountedPhaseCE : Verb_MountedShootCE
{
    protected override bool CastSingle()
    {
        bool previous=doRetarget;doRetarget=false;
        try{return base.CastSingle();}finally{doRetarget=previous;}
    }
    public override bool TryFindCEShootLineFromTo(IntVec3 root,LocalTargetInfo target,out ShootLine line,out Vector3 targetPos)
    {
        targetPos=target.HasThing?target.Thing.TrueCenter():target.Cell.ToVector3Shifted();
        line=new ShootLine(root,target.Cell);
        return caster?.Spawned==true&&root.InBounds(caster.Map)&&target.IsValid&&target.Cell.InBounds(caster.Map)
            &&(!target.HasThing||target.Thing.Spawned&&!target.Thing.Destroyed&&target.Thing!=caster
                &&target.Thing.Map==caster.Map&&Building_ModularTurret.SameFloor(caster,target.Thing))
            &&root.DistanceToSquared(target.Cell)<=EffectiveRange*EffectiveRange
            &&root.DistanceToSquared(target.Cell)>=verbProps.minRange*verbProps.minRange;
    }
}
