using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace ModularTurrets;

public class Verb_ModularShoot : Verb_ModularBurst
{
    private Func<bool> castFromMuzzle, castOpeningShot, castSingle;
    protected override bool TryCastShot()
    {
        if (caster is not Building_ModularTurret turret) return base.TryCastShot();
        return turret.WithShotMuzzle(this, EquipmentSource, castFromMuzzle ??= CastFromMuzzle);
    }
    private bool CastFromMuzzle()
        => ((Building_ModularTurret)caster).WithParagonOpeningShot(EquipmentSource, castOpeningShot ??= CastOpeningShot);
    private bool CastOpeningShot()
    {
        var turret = (Building_ModularTurret)caster;
        var original = currentTarget;
        bool fired = false;
        try
        {
            foreach (var shot in turret.BuildShots(EquipmentSource, original))
            {
                currentTarget = shot.Target;
                if (CanHitTarget(currentTarget)) fired |= turret.WithShotFactor(shot.DamageFactor, castSingle ??= CastSingle);
            }
        }
        finally { currentTarget = original; }
        return fired;
    }
    // Keep this saved verb type and its burst state when flame delivery changes.
    private bool CastSingle() => EquipmentSource?.def.GetModExtension<HeadAccuracyExtension>()?.guaranteedFlame == true
        ? GuaranteedFlameShot.TryCast(this) : base.TryCastShot();
}
