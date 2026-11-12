using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace ModularTurrets;

public sealed class Verb_ModularMortar : Verb_ModularShoot
{
    public OffMapMortarMission OffMapMission;
    public override ThingDef Projectile => caster is Building_ModularTurret turret
        ? turret.CoreMortarProjectile ?? base.Projectile : base.Projectile;
    public override void ExposeData()
    {
        base.ExposeData();
        Scribe_Deep.Look(ref OffMapMission, "modularOffMapMission");
    }
    public override bool CanHitTarget(LocalTargetInfo target)
        => MortarTargeting.CanFireAt(caster, caster?.Position ?? IntVec3.Invalid, target) && base.CanHitTarget(target);
    public override bool CanHitTargetFrom(IntVec3 root, LocalTargetInfo target)
        => MortarTargeting.CanFireAt(caster, root, target) && base.CanHitTargetFrom(root, target);
    protected override bool TryCastShot()
    {
        // Recheck every shell: a roof or a moving target can invalidate an active burst.
        if (caster is not Building_ModularTurret turret || turret.Shells <= 0 || turret.CoreMortarProjectile == null
            || !CanHitTarget(currentTarget)) return false;
        using var flight = OffMapMortar.BeginShot(this, out bool allowed);
        if (!allowed) return false;
        if (!base.TryCastShot()) return false;
        turret.ConsumeShell(); return true;
    }
}
