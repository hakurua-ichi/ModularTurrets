using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace ModularTurrets;

internal static class GuaranteedFlameShot
{
    internal static bool TryCast(Verb verb)
    {
        if (verb.caster is not Building_ModularTurret turret || !turret.Spawned) return false;
        LocalTargetInfo target = verb.CurrentTarget;
        if (!target.IsValid || target.HasThing && (!target.Thing.Spawned || target.Thing.Destroyed
            || !Building_ModularTurret.SameFloor(turret, target.Thing))
            || !verb.TryFindShootLineFromTo(turret.Position, target, out _)) return false;
        var projectile = verb.verbProps.defaultProjectile?.projectile;
        if (projectile == null) return false;
        Thing equipment = verb.EquipmentSource;
        float range = verb.EffectiveRange, angle = FlameConeGeometry.Angle(equipment);
        if (turret.ClaimFlameVisual()) FlameJetVisuals.SpawnCone(turret, turret.MuzzleFor(equipment), range, angle);
        // One snapshot per emitted shot, not one hit per visual flame ray.
        // Include collateral targets, then apply the same IFF rule individually.
        var hits = turret.Map.mapPawns.AllPawnsSpawned.Cast<Thing>()
            .Concat(turret.Map.listerBuildings.allBuildingsColonist)
            .Concat(turret.Map.listerBuildings.allBuildingsNonColonist)
            .Concat(NearbyContents(turret, range))
            .Where(hit => CanHit(verb, turret, hit, range, angle)).Distinct()
            .OrderBy(hit => hit.thingIDNumber).ToList();
        // No travel, scatter, cover or weather miss. Retain the former direct
        // hit's damage rounding and let Core Flame damage handle direct ignition.
        int damage = Math.Max(1, Mathf.RoundToInt(projectile.GetDamageAmount((Thing)null) * turret.ProjectileDamageMultiplier(equipment)));
        float armor = projectile.GetArmorPenetration((Thing)null) + turret.ProjectileArmorBonus
            + 0.05f * (turret.HeadPart?.TryGetComp<CompTurretPart>()?.Tier ?? 0) + turret.ParagonOpeningArmorBonus;
        foreach (Thing hit in hits)
        {
            if (!CanHit(verb, turret, hit, range, angle) || !turret.ClaimFlameTarget(hit)) continue;
            bool igniteContents = hit is not Pawn && hit.FlammableNow;
            IntVec3 fireCell = hit.Position;
            Map fireMap = hit.Map;
            var result = hit.TakeDamage(new DamageInfo(projectile.damageDef, damage, armor, (hit.DrawPos - turret.DrawPos).AngleFlat(),
                turret, weapon: equipment?.def, intendedTarget: hit));
            // Core checks the fuel remaining after damage and avoids duplicate
            // fires. Fire control protects direct shots, not residual fire spread.
            if (igniteContents)
                FireUtility.TryStartFireIn(fireCell, fireMap, 0.3f, turret);
            SpecialistHits.Panic(turret, equipment, hit, result);
        }
        return true;
    }

    // Limit environmental candidates to nearby cells, rather than scanning
    // every plant and stored item on the map for every shot in a burst.
    private static IEnumerable<Thing> NearbyContents(Building_ModularTurret turret, float range)
    {
        if (!float.IsFinite(range) || range <= 0f) yield break;
        Vector3 center = turret.DrawPos;
        var map = turret.Map;
        int minX = Mathf.FloorToInt(Math.Max(0f, center.x - range));
        int maxX = Mathf.FloorToInt(Math.Min(map.Size.x - 1f, center.x + range));
        int minZ = Mathf.FloorToInt(Math.Max(0f, center.z - range));
        int maxZ = Mathf.FloorToInt(Math.Min(map.Size.z - 1f, center.z + range));
        for (int z = minZ; z <= maxZ; z++)
            for (int x = minX; x <= maxX; x++)
                foreach (var thing in new IntVec3(x, 0, z).GetThingList(map))
                    if (thing is Plant || thing.def.category == ThingCategory.Item)
                        yield return thing;
    }

    private static bool CanHit(Verb verb, Building_ModularTurret turret, Thing hit, float range, float angle)
        => hit != turret && hit.Spawned && !hit.Destroyed && hit.Map == turret.Map
            && (hit is Building || hit is Pawn pawn && !pawn.Dead
                || (hit is Plant || hit.def.category == ThingCategory.Item) && hit.def.useHitPoints && hit.FlammableNow)
            && Building_ModularTurret.SameFloor(turret, hit)
            && (!turret.PreventFriendlyFire || !FriendlyFireSafety.IsProtectedPawn(hit, turret.Faction))
            && FlameConeGeometry.Contains(turret.DrawPos, turret.AimAngle, angle, range, hit.DrawPos)
            && verb.TryFindShootLineFromTo(turret.Position, hit, out _);
}
