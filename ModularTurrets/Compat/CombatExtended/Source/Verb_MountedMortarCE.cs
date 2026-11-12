using System;
using CombatExtended;
using UnityEngine;
using Verse;

namespace ModularTurrets.Compat.CombatExtended;

public partial class Verb_MountedMortarCE : Verb_ShootMortarCE,IMountedAiming
{
    private Func<bool> castScoped,castOpening;
    private MountedAimState mountedAim=new();
    private Action completeAim;
    public override void WarmupComplete()
    {
        if(caster is Building_ModularTurret owner && mountedAim.Begin(ShouldAim,
            (currentTarget.Cell-caster.Position).LengthHorizontal,owner.MortarSupportWarmupFactor)) return;
        base.WarmupComplete();
    }
    public bool TickMountedAim(){if(mountedAim.Pending&&!ShouldAim)mountedAim.Ticks=0;return mountedAim.Tick(completeAim ??= WarmupComplete);}
    public override void RecalculateWarmupTicks()
    {if(caster is Building_ModularTurret)MountedWarmup.Reduce(this,mountedAim,lastShotRotation,ref lastRecoilDeg,ref storedShotReduction);else base.RecalculateWarmupTicks();}
    public override void Reset(){base.Reset();mountedAim?.Reset();}
    private void ExposeMountedAim(){Scribe_Deep.Look(ref mountedAim,"mtCEAiming");mountedAim??=new();}
    public override float ShotHeight => MountedGeometry.Height(caster,base.ShotHeight);
    public override float SwayAmplitude => base.SwayAmplitude*MountedGeometry.Accuracy(EquipmentSource);
    public override bool TryCastShot()
        => caster is Building_ModularTurret owner?owner.WithShotMuzzle(this,EquipmentSource,castScoped ??= CastScoped):base.TryCastShot();
    private bool CastScoped()=>((Building_ModularTurret)caster).WithParagonOpeningShot(EquipmentSource,castOpening ??= CastOpening);
    private bool CastOpening()
    {
        if(CompAmmo is {CanBeFiredNow:false})return false;
        if(globalTargetInfo.IsValid&&!WorldReadyToCast())return false;
        var owner=(Building_ModularTurret)caster;var target=currentTarget;bool fired=false;
        try{foreach(var shot in owner.BuildShots(EquipmentSource,target)){currentTarget=shot.Target;if(globalTargetInfo.IsValid||CanHitTarget(currentTarget))fired|=owner.WithShotFactor(shot.DamageFactor,CastSingle);}}
        finally{currentTarget=target;}return fired;
    }
    private bool CastSingle()
    {
        // CE's 81mm shell has speed0; the gun's normal charge component selects
        // its ballistic speed. Never launch a stationary shell on missing input.
        float speed=ShotSpeed;
        if(!float.IsFinite(speed)||speed<=0f)return false;
        return !globalTargetInfo.IsValid||CompAmmo?.TryPrepareShot()!=false?base.TryCastShot():false;
    }
    public override void ShiftTarget(ShiftVecReport report,Vector3 target,out float height,bool mechanicalOnly=false,bool instant=false)
    {base.ShiftTarget(report,target,out height,mechanicalOnly,instant);if(caster is Building_ModularTurret){Vector3 p=MountedGeometry.Origin(caster,EquipmentSource,caster.DrawPos,ShotHeight);sourceLoc=new Vector2(p.x,p.z);}}
    protected override float ShotAngle(Vector3 source,Vector3 target)=>base.ShotAngle(MountedGeometry.Origin(caster,EquipmentSource,source,ShotHeight),target);
    protected override float ShotRotation(Vector3 source,Vector3 target)=>base.ShotRotation(MountedGeometry.Origin(caster,EquipmentSource,source,ShotHeight),target);
    protected override bool CanHitCellFromCellIgnoringRange(Vector3 source,IntVec3 target,Thing thing=null)
        =>base.CanHitCellFromCellIgnoringRange(MountedGeometry.Origin(caster,EquipmentSource,source,ShotHeight),target,thing);
}
