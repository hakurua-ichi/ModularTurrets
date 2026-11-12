using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace ModularTurrets;

public sealed class Verb_Tesla : Verb_Shoot
{
    private Func<bool> castFromMuzzle, castOpeningShot;
    protected override bool TryCastShot()
    {
        if (caster is not Building_ModularTurret turret || !currentTarget.IsValid || !TryFindShootLineFromTo(caster.Position, currentTarget, out _)) return false;
        return turret.WithShotMuzzle(this, EquipmentSource, castFromMuzzle ??= CastFromMuzzle);
    }
    private bool CastFromMuzzle()
        => ((Building_ModularTurret)caster).WithParagonOpeningShot(EquipmentSource, castOpeningShot ??= CastOpeningShot);
    private bool CastOpeningShot()
    {
        var turret = (Building_ModularTurret)caster;
        var p = verbProps.defaultProjectile.projectile;
        float range = EffectiveRange;
        // Enumerate map targets instead of GenRadial's finite lookup table so
        // late-game range upgrades still include the entire circular range.
        var targets = caster.Map.mapPawns.AllPawnsSpawned.Cast<Thing>()
            .Concat(caster.Map.listerBuildings.allBuildingsColonist)
            .Concat(caster.Map.listerBuildings.allBuildingsNonColonist)
            .Where(t => CanPulseHit(t, range)).Distinct()
            .OrderBy(t => t.Position.DistanceToSquared(caster.Position)).ToList();
        TeslaPulseVisuals.Spawn(turret, range);
        foreach (var t in targets)
        {
            // Earlier damage may have despawned a later target. Each surviving
            // eligible Thing receives this pulse once, independent of chain count.
            if (!CanPulseHit(t, range)) continue;
            var result = t.TakeDamage(new DamageInfo(p.damageDef, p.GetDamageAmount(EquipmentSource) * turret.ProjectileDamageMultiplier(EquipmentSource),
                p.GetArmorPenetration(EquipmentSource) + turret.ProjectileArmorBonus + 0.05f * (turret.HeadPart?.TryGetComp<CompTurretPart>()?.Tier ?? 0) + turret.ParagonOpeningArmorBonus,
                (t.DrawPos - caster.DrawPos).AngleFlat(), caster, weapon: EquipmentSource?.def, intendedTarget: t));
            SpecialistHits.Pulse(turret, EquipmentSource, t, result);
        }
        return true;
    }
    private bool CanPulseHit(Thing target, float range)
        => target.Spawned && !target.Destroyed && target != caster
            && Building_ModularTurret.SameFloor(caster, target)
            && (caster is not Building_ModularTurret turret || !turret.PreventFriendlyFire
                || !FriendlyFireSafety.IsProtectedPawn(target, turret.Faction))
            // Pulse collateral is independent of automatic enemy acquisition.
            // Friendly buildings retain their previous exclusion. Living pawns
            // are protected only by the currently fitted fire-control module.
            && (target is Building && target.HostileTo(caster) || target is Pawn pawn && !pawn.Dead)
            && target.Position.DistanceToSquared(caster.Position) <= range * range && CanHitTarget(target);
}
