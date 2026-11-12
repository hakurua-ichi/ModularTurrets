using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace ModularTurrets;

public sealed class Projectile_ModularBullet : Bullet
{
    private float damageScale = 1f, armorBonus;
    private Faction firingFaction;
    private float specialistExplosionRadius, penetrationFalloff = 1f;
    private int penetrationRemaining, continuationTicks;
    private Vector3 penetrationEnd, continuationStart;
    private List<int> penetratedIds;
    internal Faction SavedProtectionFaction => preventFriendlyFire ? firingFaction : null;
    internal bool HasAlreadyPenetrated(Thing thing) => thing != null && penetratedIds != null
        && (penetratedIds.Contains(thing.thingIDNumber)
            || thing is Corpse corpse && corpse.InnerPawn != null && penetratedIds.Contains(corpse.InnerPawn.thingIDNumber));
    // Continue from the contact point without moving the original launch origin.
    // Core's interception-distance checks and projectile-interceptor shields
    // therefore remain active throughout the same projectile's flight.
    public override Vector3 ExactPosition => continuationTicks <= 0 ? base.ExactPosition
        : Vector3.Lerp(continuationStart, destination, Mathf.Clamp01(1f - (float)ticksToImpact / continuationTicks)).Yto0()
            + Vector3.up * def.Altitude;
    public override int UpdateRateTicks => penetrationRemaining > 0 ? 1 : base.UpdateRateTicks;
    public override int DamageAmount => Math.Max(1, Mathf.RoundToInt(base.DamageAmount * damageScale));
    public override float ArmorPenetration => base.ArmorPenetration + armorBonus;
    public override void Launch(Thing launcher, Vector3 origin, LocalTargetInfo usedTarget, LocalTargetInfo intendedTarget, ProjectileHitFlags hitFlags, bool preventFriendlyFire = false, Thing equipment = null, ThingDef targetCoverDef = null)
    {
        SpecialistModuleExtension specialist = null;
        float penetrationRange = 0f;
        if (launcher is Building_ModularTurret turret)
        {
            damageScale = turret.ProjectileDamageMultiplier(equipment); armorBonus = turret.ProjectileArmorBonus + 0.05f * (turret.HeadPart?.TryGetComp<CompTurretPart>()?.Tier ?? 0) + turret.ParagonOpeningArmorBonus;
            origin = turret.MuzzleFor(equipment);
            preventFriendlyFire |= turret.PreventFriendlyFire;
            specialist = turret.SpecialistFor(equipment);
            penetrationRange = turret.AttackVerb?.EffectiveRange ?? 0f;
        }
        firingFaction = preventFriendlyFire ? launcher?.Faction : null;
        base.Launch(launcher, origin, usedTarget, intendedTarget, hitFlags, preventFriendlyFire, equipment, targetCoverDef);
        if (specialist?.kind == "Explosive") specialistExplosionRadius = SpecialistModuleExtension.Factor(specialist.explosionRadius, 1.4f);
        else if (specialist?.kind == "Penetrator")
        {
            penetrationRemaining = Math.Clamp(specialist.penetrationTargets, 1, 32);
            penetrationFalloff = float.IsFinite(specialist.penetrationDamageFactor) ? Mathf.Clamp01(specialist.penetrationDamageFactor) : 0.7f;
            penetratedIds = new List<int>();
            Vector3 ray = (destination - this.origin).Yto0();
            float range = float.IsFinite(penetrationRange) ? Math.Max(ray.magnitude, penetrationRange) : ray.magnitude;
            Vector3 direction = ray.normalized;
            if (Map != null)
            {
                if (direction.x > 0.00001f) range = Math.Min(range, (Map.Size.x - 0.01f - this.origin.x) / direction.x);
                else if (direction.x < -0.00001f) range = Math.Min(range, (0.01f - this.origin.x) / direction.x);
                if (direction.z > 0.00001f) range = Math.Min(range, (Map.Size.z - 0.01f - this.origin.z) / direction.z);
                else if (direction.z < -0.00001f) range = Math.Min(range, (0.01f - this.origin.z) / direction.z);
            }
            penetrationEnd = this.origin.Yto0() + direction * Math.Max(0f, range);
        }
        // Snapshot the upgrade at launch; a removed runtime gun is not a save reference.
        this.equipment = null;
    }
    public override void ExposeData()
    {
        base.ExposeData(); Scribe_Values.Look(ref damageScale, "modularDamageScale", 1f); Scribe_Values.Look(ref armorBonus, "modularArmorBonus");
        Scribe_References.Look(ref firingFaction, "modularFiringFaction");
        Scribe_Values.Look(ref specialistExplosionRadius, "specialistExplosionRadius");
        Scribe_Values.Look(ref penetrationRemaining, "specialistPenetrationRemaining");
        Scribe_Values.Look(ref penetrationFalloff, "specialistPenetrationFalloff", 1f);
        Scribe_Values.Look(ref penetrationEnd, "specialistPenetrationEnd");
        Scribe_Values.Look(ref continuationStart, "specialistContinuationStart");
        Scribe_Values.Look(ref continuationTicks, "specialistContinuationTicks");
        Scribe_Collections.Look(ref penetratedIds, "specialistPenetratedIds", LookMode.Value);
    }
    protected override void Impact(Thing hitThing, bool blockedByShield = false)
    {
        // Core already skips friendly pawns along the flight path. Its final
        // impact picker does not check that flag, so a friendly endpoint is a miss.
        if (!blockedByShield && preventFriendlyFire && FriendlyFireSafety.IsProtectedPawn(hitThing, firingFaction ?? launcher?.Faction))
            hitThing = null;
        if (!blockedByShield && specialistExplosionRadius > 0f)
        {
            Map map = Map;
            IntVec3 cell = hitThing?.Position ?? ExactPosition.ToIntVec3();
            var ignored = preventFriendlyFire ? FriendlyFireSafety.ProtectedPawns(map, firingFaction ?? launcher?.Faction) : null;
            int damage = DamageAmount; float armor = ArmorPenetration;
            Destroy();
            if (cell.InBounds(map)) GenExplosion.DoExplosion(cell, map, specialistExplosionRadius, DamageDefOf.Bomb,
                launcher, damage, armor, weapon: equipmentDef, projectile: def, intendedTarget: intendedTarget.Thing,
                ignoredThings: ignored, damageFalloff: true);
            return;
        }
        if (!blockedByShield && penetrationRemaining > 0 && hitThing is Pawn pawn && !HasAlreadyPenetrated(pawn))
        {
            ImpactPenetrator(pawn);
            return;
        }
        base.Impact(hitThing, blockedByShield);
    }

    private void ImpactPenetrator(Pawn pawn)
    {
        // Bullet.Impact destroys its projectile before applying damage. This
        // branch applies that one impact explicitly and retains this same Thing
        // only when the pawn took damage and another penetration is allowed.
        Map map = Map;
        Vector3 previousPosition = ExactPosition.Yto0();
        Vector3 direction = (penetrationEnd - origin).Yto0().normalized;
        float along = Vector3.Dot((pawn.DrawPos - origin).Yto0(), direction);
        Vector3 contact = origin.Yto0() + direction * Math.Clamp(along, 0f, (previousPosition - origin).MagnitudeHorizontal());
        var log = new BattleLogEntry_RangedImpact(launcher, pawn, intendedTarget.Thing, equipmentDef, def, targetCoverDef);
        Find.BattleLog.Add(log);
        GenClamor.DoClamor(this, 12f, ClamorDefOf.Impact);
        NotifyPenetratorImpact(pawn, map, Position);
        var info = new DamageInfo(DamageDef, DamageAmount, ArmorPenetration, ExactRotation.eulerAngles.y,
            launcher, weapon: equipmentDef, intendedTarget: intendedTarget.Thing);
        info.SetWeaponQuality(equipmentQuality);
        var result = pawn.TakeDamage(info);
        result.AssociateWithLog(log);
        pawn.stances?.stagger.Notify_BulletImpact(this);
        // Core constructs a concatenation iterator on each property read.
        var extras = ExtraDamages;
        if (extras != null)
            foreach (ExtraDamage extra in extras)
                if (Rand.Chance(extra.chance))
                    pawn.TakeDamage(new DamageInfo(extra.def, extra.amount, extra.AdjustedArmorPenetration(),
                        ExactRotation.eulerAngles.y, launcher, weapon: equipmentDef, intendedTarget: intendedTarget.Thing))
                        .AssociateWithLog(log);
        penetratedIds ??= new List<int>();
        penetratedIds.Add(pawn.thingIDNumber);
        penetrationRemaining--;
        if (result.totalDamageDealt <= 0f || penetrationRemaining <= 0 || penetrationFalloff <= 0f
            || (penetrationEnd - contact).MagnitudeHorizontal() <= 0.05f)
        {
            Destroy();
            return;
        }
        damageScale *= penetrationFalloff;
        continuationStart = contact;
        destination = penetrationEnd;
        usedTarget = new LocalTargetInfo(destination.ToIntVec3());
        HitFlags |= ProjectileHitFlags.NonTargetPawns | ProjectileHitFlags.NonTargetWorld;
        continuationTicks = Math.Max(1, Mathf.CeilToInt((destination - continuationStart).MagnitudeHorizontal() / def.projectile.SpeedTilesPerTick));
        ticksToImpact = lifetime = continuationTicks;
        Position = continuationStart.ToIntVec3();
    }

    private void NotifyPenetratorImpact(Thing hitThing, Map map, IntVec3 position)
    {
        // Preserve Bullet's direct and nearby impact notifications while this
        // projectile survives; animal reactions and other comps depend on them.
        var impact = new BulletImpactData { bullet = this, hitThing = hitThing, impactPosition = position };
        hitThing?.Notify_BulletImpactNearby(impact);
        for (int i = 0; i < 9; i++)
        {
            IntVec3 cell = position + GenRadial.RadialPattern[i];
            if (!cell.InBounds(map)) continue;
            List<Thing> things = cell.GetThingList(map);
            for (int j = 0; j < things.Count; j++)
                if (things[j] != hitThing) things[j].Notify_BulletImpactNearby(impact);
        }
    }
}
