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

public abstract class Projectile_MountedFieldCE : BulletCE
{
    public override void DrawAt(Vector3 drawLoc,bool flip=false){}
    protected void Prepare(Thing launcher,Thing equipment,Vector2 origin,float shotAngle,float shotRotation,float shotHeight,float shotSpeed)
    {
        this.launcher=launcher;this.equipment=equipment;equipmentDef=equipment?.def;this.origin=origin;OriginIV3=new IntVec3(origin);
        this.shotAngle=shotAngle;this.shotRotation=shotRotation;this.shotHeight=shotHeight;this.shotSpeed=shotSpeed;
        ExactPosition=LastPos=new Vector3(origin.x,shotHeight,origin.y);castShadow=false;logMisses=false;
        // These overrides do not call CE's base RayCast, so its prefix does not run.
        ShotSnapshots.Capture(this,launcher,equipment);
        if(!ShotSnapshots.TryGet(this,out _))throw new InvalidOperationException("MT CE field shot has no departure snapshot.");
    }
    public override void Impact(Thing hitThing)
    {
        if(hitThing!=null&&ShotSnapshots.IsProtected(hitThing,this)){Destroy();return;}
        using var effects=new SpecialWeapons.ImpactScope(this);
        base.Impact(hitThing);
    }
}
