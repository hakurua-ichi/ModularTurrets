using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace ModularTurrets;

public class Verb_Instant : Verb_ModularBurst
{
    private Func<bool> castFromMuzzle, castOpeningShot;
    protected virtual bool IgnoresObstacles => false;
    protected override bool TryCastShot()
    {
        if (caster is not Building_ModularTurret turret || !currentTarget.IsValid || currentTarget.HasThing && !Building_ModularTurret.SameFloor(turret, currentTarget.Thing)
            || !TryFindShootLineFromTo(caster.Position, currentTarget, out _)) return false;
        return turret.WithShotMuzzle(this, EquipmentSource, castFromMuzzle ??= CastFromMuzzle);
    }
    private bool CastFromMuzzle()
        => ((Building_ModularTurret)caster).WithParagonOpeningShot(EquipmentSource, castOpeningShot ??= CastOpeningShot);
    private bool CastOpeningShot()
    {
        var turret = (Building_ModularTurret)caster;
        bool protectAllies = ProtectAllies(turret);
        foreach (var shot in turret.BuildShots(EquipmentSource, currentTarget))
        {
            LocalTargetInfo target = shot.Target;
            if (shot.IsBranch && !IgnoresObstacles)
            {
                foreach (var cell in GenSight.PointsOnLineOfSight(caster.Position, target.Cell))
                {
                    if (cell == caster.Position || !cell.InBounds(caster.Map)) continue;
                    var hitOnRay = cell.GetThingList(caster.Map).FirstOrDefault(t => t != caster && (t is Pawn || t.def.Fillage == FillCategory.Full)
                        && Building_ModularTurret.SameFloor(caster, t) && (!protectAllies || !FriendlyFireSafety.IsProtectedPawn(t, caster.Faction)));
                    if (hitOnRay != null) { target = hitOnRay; break; }
                }
            }
            if (!TryFindShootLineFromTo(caster.Position, target, out _)) continue;
            var report = ShotReport.HitReportFor(caster, this, target);
            Thing hit = target.Thing;
            if (!Rand.Chance(report.AimOnTargetChance_IgnoringPosture)) hit = null;
            else if (!IgnoresObstacles && hit?.def.CanBenefitFromCover == true && !Rand.Chance(report.PassCoverChance)) hit = report.GetRandomCoverToMissInto();
            if (hit != null && !Building_ModularTurret.SameFloor(caster, hit)) hit = null;
            if (protectAllies && FriendlyFireSafety.IsProtectedPawn(hit, caster.Faction)) hit = null;
            ShowShot(turret, hit?.DrawPos ?? target.CenterVector3, hit != null);
            if (hit == null) continue;
            var p = verbProps.defaultProjectile.projectile;
            float damage = p.GetDamageAmount(EquipmentSource) * turret.ProjectileDamageMultiplier(EquipmentSource) * shot.DamageFactor;
            float ap = p.GetArmorPenetration(EquipmentSource) + turret.ProjectileArmorBonus + 0.05f * (turret.HeadPart?.TryGetComp<CompTurretPart>()?.Tier ?? 0);
            var specialist = turret.SpecialistFor(EquipmentSource);
            if (specialist?.kind == "Quantum") ap = Math.Max(ap, SpecialistModuleExtension.Factor(specialist.minimumArmorPenetration, 1f));
            ap += turret.ParagonOpeningArmorBonus;
            var info = new DamageInfo(p.damageDef, damage, ap, (hit.DrawPos - caster.DrawPos).AngleFlat(), caster, weapon: EquipmentSource?.def, intendedTarget: target.Thing);
            var result = hit.TakeDamage(info);
            SpecialistHits.Disrupt(turret, EquipmentSource, hit, info, result);
        }
        return true;
    }
    protected virtual void ShowShot(Building_ModularTurret turret, Vector3 end, bool hit)
        => turret.ShowBeam(turret.MuzzleFor(EquipmentSource), end, EquipmentSource?.def.GetModExtension<BeamVisualExtension>());
    protected virtual bool ProtectAllies(Building_ModularTurret turret) => turret.PreventFriendlyFire;
}
