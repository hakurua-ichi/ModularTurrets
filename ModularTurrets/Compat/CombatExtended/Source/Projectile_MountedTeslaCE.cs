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

public sealed class Projectile_MountedTeslaCE : Projectile_MountedFieldCE
{
    public override void RayCast(Thing launcher,VerbProperties verbProps,Vector2 origin,float shotAngle,float shotRotation,
        float shotHeight=0f,float shotSpeed=-1f,float spreadDegrees=0f,float aperatureSize=0.03f,Thing equipment=null,bool useSameHeight=false)
    {
        try
        {
            Prepare(launcher,equipment,origin,shotAngle,shotRotation,shotHeight,shotSpeed);
            if(launcher is not Building_ModularTurret owner)throw new InvalidOperationException("MT CE Tesla requires its mounted owner.");
            float range=verbProps.range;
            var targets=Map.mapPawns.AllPawnsSpawned.Cast<Thing>().Concat(Map.listerBuildings.allBuildingsColonist)
                .Concat(Map.listerBuildings.allBuildingsNonColonist).Where(t=>CanPulseHit(owner,t,range)).Distinct()
                .OrderBy(t=>t.Position.DistanceToSquared(owner.Position)).ToList();
            SpecialWeapons.ShowPulse(owner,range);
            var hitDef=DefDatabase<ThingDef>.GetNamed("MT_CE_Projectile_TeslaHit");
            foreach(var hit in targets)
            {
                if(!CanPulseHit(owner,hit,range))continue;
                var child=(Projectile_MountedPulseHitCE)ThingMaker.MakeThing(hitDef);
                GenSpawn.Spawn(child,hit.Position,Map);
                try{using(ShotSnapshots.UseSecondary(this))child.Apply(this,hit);}
                finally{if(child.Spawned&&!child.Destroyed)child.Destroy();}
                SpecialWeapons.PulseHits++;
            }
        }
        finally{if(Spawned&&!Destroyed)Destroy();}
    }
    private bool CanPulseHit(Building_ModularTurret owner,Thing hit,float range)
        =>hit!=owner&&hit.Spawned&&!hit.Destroyed&&hit.Map==Map&&Building_ModularTurret.SameFloor(owner,hit)
            &&!ShotSnapshots.IsProtected(hit,this)&&(hit is Building&&hit.HostileTo(owner)||hit is Pawn pawn&&!pawn.Dead)
            &&hit.Position.DistanceToSquared(owner.Position)<=range*range;
}
