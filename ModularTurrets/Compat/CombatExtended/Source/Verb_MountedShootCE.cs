using System;
using CombatExtended;
using UnityEngine;
using Verse;

namespace ModularTurrets.Compat.CombatExtended;

// CE retains its targeting, scatter, energy/penetration, ammo and projectile code.
// Only the physical mount origin/height and MT-owned effect shot expansion differ.
public class Verb_MountedShootCE : Verb_ShootCE,IMountedAiming
{
    private Func<bool> castScoped,castOpening;
    private MountedAimState mountedAim=new();
    private Action completeAim;
    public override void WarmupComplete()
    {
        if(caster is Building_ModularTurret&&mountedAim.Begin(ShouldAim,(currentTarget.Cell-caster.Position).LengthHorizontal))return;
        base.WarmupComplete();
    }
    public bool TickMountedAim(){if(mountedAim.Pending&&!ShouldAim)mountedAim.Ticks=0;return mountedAim.Tick(completeAim ??= WarmupComplete);}
    public override void RecalculateWarmupTicks()
    {if(caster is Building_ModularTurret)MountedWarmup.Reduce(this,mountedAim,lastShotRotation,ref lastRecoilDeg,ref storedShotReduction);else base.RecalculateWarmupTicks();}
    public override void Reset(){base.Reset();mountedAim?.Reset();}
    public override void ExposeData(){base.ExposeData();Scribe_Deep.Look(ref mountedAim,"mtCEAiming");mountedAim??=new();}
    public override float ShotHeight => MountedGeometry.Height(caster,base.ShotHeight);
    public override float SwayAmplitude => base.SwayAmplitude*MountedGeometry.Accuracy(EquipmentSource);
    public override bool TryCastShot()
        => caster is Building_ModularTurret owner?owner.WithShotMuzzle(this,EquipmentSource,castScoped ??= CastScoped):base.TryCastShot();
    private bool CastScoped()=>((Building_ModularTurret)caster).WithParagonOpeningShot(EquipmentSource,castOpening ??= CastOpening);
    private bool CastOpening()
    {
        var owner=(Building_ModularTurret)caster;var target=currentTarget;bool fired=false;
        try{foreach(var shot in owner.BuildShots(EquipmentSource,target)){currentTarget=shot.Target;if(CanHitTarget(currentTarget))fired|=owner.WithShotFactor(shot.DamageFactor,CastSingle);}}
        finally{currentTarget=target;}return fired;
    }
    protected virtual bool CastSingle()=>base.TryCastShot();
    public override ShiftVecReport ShiftVecReportFor(LocalTargetInfo target,IntVec3 targetCell)
    {
        var report=base.ShiftVecReportFor(target,targetCell);
        // CE's instant cover scan reaches the source cell. A multi-cell mounted
        // caster is not cover in front of its own target; keep every other field.
        if(caster is Building_ModularTurret&&report.cover==caster)
            report.cover=MountedGeometry.CoverWithoutCaster(caster,target,projectilePropsCE.isInstant);
        return report;
    }
    public override void ShiftTarget(ShiftVecReport report,Vector3 target,out float height,bool mechanicalOnly=false,bool instant=false)
    {base.ShiftTarget(report,target,out height,mechanicalOnly,instant);if(caster is Building_ModularTurret){Vector3 p=MountedGeometry.Origin(caster,EquipmentSource,caster.DrawPos,ShotHeight);sourceLoc=new Vector2(p.x,p.z);}}
    protected override float ShotAngle(Vector3 source,Vector3 target)=>base.ShotAngle(MountedGeometry.Origin(caster,EquipmentSource,source,ShotHeight),target);
    protected override float ShotRotation(Vector3 source,Vector3 target)=>base.ShotRotation(MountedGeometry.Origin(caster,EquipmentSource,source,ShotHeight),target);
    protected override bool CanHitCellFromCellIgnoringRange(Vector3 source,IntVec3 target,Thing thing=null)
    {
        var origin=MountedGeometry.Origin(caster,EquipmentSource,source,ShotHeight);
        if(caster is Building_ModularTurret owner&&verbProps.requireLineOfSight&&!Projectile.projectile.flyOverhead
            &&!MountedGeometry.SingleAdjacentSolid(owner,origin,target,thing))return false;
        return base.CanHitCellFromCellIgnoringRange(origin,target,thing);
    }
}
