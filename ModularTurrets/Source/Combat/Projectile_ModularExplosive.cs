using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace ModularTurrets;

public partial class Projectile_ModularExplosive
{
    protected override void Explode()
    {
        if (!preventFriendlyFire && mortarExplosionRadiusFactor == 1f) { base.Explode(); return; }
        Map map = Map;
        float radius = def.projectile.explosionRadius * mortarExplosionRadiusFactor;
        var ignored = preventFriendlyFire ? FriendlyFireSafety.ProtectedPawns(map, firingFaction ?? launcher?.Faction,
            ProtectsMortarBuildings, Position, radius) : null;
        Destroy();
        var p = def.projectile;
        // Preserve Core's options with this shot's radius and optional IFF list.
        if (p.explosionEffect != null)
        {
            Effecter effecter = p.explosionEffect.Spawn();
            if (p.explosionEffectLifetimeTicks != 0)
                map.effecterMaintainer.AddEffecterToMaintain(effecter, Position.ToVector3().ToIntVec3(), p.explosionEffectLifetimeTicks);
            else
            {
                effecter.Trigger(new TargetInfo(Position, map), new TargetInfo(Position, map));
                effecter.Cleanup();
            }
        }
        GenExplosion.DoExplosion(Position, map, radius, DamageDef, launcher, DamageAmount, ArmorPenetration,
            explosionSound: p.soundExplode, weapon: equipmentDef, projectile: def, intendedTarget: intendedTarget.Thing,
            postExplosionSpawnThingDef: p.postExplosionSpawnThingDef ?? (p.explosionSpawnsSingleFilth ? null : p.filth),
            postExplosionSpawnChance: p.postExplosionSpawnChance, postExplosionSpawnThingCount: p.postExplosionSpawnThingCount,
            postExplosionGasType: p.postExplosionGasType, postExplosionGasRadiusOverride: null, postExplosionGasAmount: 255,
            applyDamageToExplosionCellsNeighbors: p.applyDamageToExplosionCellsNeighbors,
            preExplosionSpawnThingDef: p.preExplosionSpawnThingDef, preExplosionSpawnChance: p.preExplosionSpawnChance,
            preExplosionSpawnThingCount: p.preExplosionSpawnThingCount, chanceToStartFire: p.explosionChanceToStartFire,
            damageFalloff: p.explosionDamageFalloff, direction: origin.AngleToFlat(destination), ignoredThings: ignored,
            affectedAngle: null, doVisualEffects: p.doExplosionVFX, propagationSpeed: DamageDef.expolosionPropagationSpeed,
            excludeRadius: 0f, doSoundEffects: true, postExplosionSpawnThingDefWater: p.postExplosionSpawnThingDefWater,
            screenShakeFactor: p.screenShakeFactor, flammabilityChanceCurve: null, overrideCells: null,
            postExplosionSpawnSingleThingDef: p.postExplosionSpawnSingleThingDef,
            preExplosionSpawnSingleThingDef: p.preExplosionSpawnSingleThingDef);
        if (p.explosionSpawnsSingleFilth && p.filth != null && p.filthCount.TrueMax > 0 && Rand.Chance(p.filthChance) && !Position.Filled(map))
            FilthMaker.TryMakeFilth(Position, map, p.filth, p.filthCount.RandomInRange);
    }
}
