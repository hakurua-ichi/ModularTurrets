using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace ModularTurrets;

public partial class Projectile_ModularExplosive : Projectile_Explosive
{
    private float damageScale = 1f, armorBonus;
    private float mortarExplosionRadiusFactor = 1f;
    private int damageBase = -1;
    private Faction firingFaction;
    internal Faction SavedProtectionFaction => preventFriendlyFire ? firingFaction : null;
    internal bool ProtectsMortarBuildings => preventFriendlyFire && GetType() == typeof(Projectile_ModularExplosive)
        && MortarShellProjectiles.IsMortarProjectile(def);
    public override int DamageAmount => Math.Max(0, Mathf.RoundToInt((damageBase >= 0 ? damageBase : base.DamageAmount) * damageScale));
    public override float ArmorPenetration => base.ArmorPenetration + armorBonus;
    public override void Launch(Thing launcher, Vector3 origin, LocalTargetInfo usedTarget, LocalTargetInfo intendedTarget, ProjectileHitFlags hitFlags, bool preventFriendlyFire = false, Thing equipment = null, ThingDef targetCoverDef = null)
    {
        if (launcher is Building_ModularTurret turret)
        {
            mortarExplosionRadiusFactor = turret.MortarExplosionRadiusFactor(equipment);
            damageScale = turret.ProjectileDamageMultiplier(equipment); armorBonus = turret.ProjectileArmorBonus + 0.05f * (turret.HeadPart?.TryGetComp<CompTurretPart>()?.Tier ?? 0) + turret.ParagonOpeningArmorBonus; origin = turret.MuzzleFor(equipment);
            preventFriendlyFire |= turret.PreventFriendlyFire;
        }
        firingFaction = preventFriendlyFire ? launcher?.Faction : null;
        base.Launch(launcher, origin, usedTarget, intendedTarget, hitFlags, preventFriendlyFire, equipment, targetCoverDef);
        this.equipment = null;
        damageBase = base.DamageAmount;
        OffMapMortar.Transfer(this, launcher, equipment);
    }
    public override void ExposeData()
    {
        MortarProjectileSaveCompatibility.NormalizeDef(this);
        base.ExposeData(); Scribe_Values.Look(ref damageScale, "modularDamageScale", 1f); Scribe_Values.Look(ref armorBonus, "modularArmorBonus");
        Scribe_References.Look(ref firingFaction, "modularFiringFaction");
        Scribe_Values.Look(ref damageBase, "modularDamageBase", -1);
        Scribe_Values.Look(ref mortarExplosionRadiusFactor, "modularMortarExplosionRadiusFactor", 1f);
        // Old in-flight 2x24 missiles retain their original payload and straight flight.
        if (Scribe.mode == LoadSaveMode.PostLoadInit && damageBase < 0
            && def?.defName == "MT_Projectile_Missile") damageBase = 24;
    }
}
