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

public sealed class Projectile_MountedQuantumCE : Projectile_MountedFieldCE
{
    private float minimumSharp;
    public override float PenetrationAmount
    {
        get
        {
            float value=base.PenetrationAmount;
            if(minimumSharp<=0f)return value;
            float factor=ShotSnapshots.TryGet(this,out var record)?record.ArmorFactor:1f;
            return Math.Max(value,minimumSharp*factor*(equipment?.GetStatValue(StatDefOf.RangedWeapon_ArmorPenetrationMultiplier)??1f));
        }
    }
    public override void RayCast(Thing launcher,VerbProperties verbProps,Vector2 origin,float shotAngle,float shotRotation,
        float shotHeight=0f,float shotSpeed=-1f,float spreadDegrees=0f,float aperatureSize=0.03f,Thing equipment=null,bool useSameHeight=false)
    {
        Prepare(launcher,equipment,origin,shotAngle,shotRotation,shotHeight,shotSpeed);
        if(launcher is not Building_ModularTurret owner)throw new InvalidOperationException("MT CE quantum requires its mounted owner.");
        // 24 mm RHA is an explicit CE budget (charged AP), never a conversion of
        // the vanilla module's AP=1. Ordinary quantum shots use the Def's 16 mm.
        minimumSharp=owner.SpecialistFor(equipment)?.kind=="Quantum"?24f:0f;
        Thing hit=intendedTargetThing;
        float rotation=shotRotation*Mathf.Deg2Rad+Mathf.PI/2f;
        var direction=new Vector3(Mathf.Cos(rotation)*Mathf.Cos(shotAngle),Mathf.Sin(shotAngle),Mathf.Sin(rotation)*Mathf.Cos(shotAngle));
        var ray=new Ray(ExactPosition,direction);
        if(hit==null||!hit.Spawned||hit.Destroyed||hit.Map!=Map||!Building_ModularTurret.SameFloor(owner,hit)
            ||ShotSnapshots.IsProtected(hit,this)||!CE_Utility.GetBoundsFor(hit).IntersectRay(ray,out float distance)
            ||distance<0f||distance>verbProps.range){Destroy();return;}
        ExactPosition=ray.GetPoint(distance);Destination=new Vector2(ExactPosition.x,ExactPosition.z);LastPos=ExactPosition;
        // Phase through intervening objects, but retain CE's actual target bounds,
        // randomized aim, physical body height and ordinary CE BulletCE.Impact.
        FleckMaker.Static(hit.DrawPos,Map,FleckDefOf.ExplosionFlash,0.35f);SpecialWeapons.QuantumHits++;
        Impact(hit);
    }
    public override void ExposeData(){base.ExposeData();Scribe_Values.Look(ref minimumSharp,"mtCEQuantumMinimumSharp");}
}
