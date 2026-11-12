using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using CombatExtended;
using CombatExtended.Compatibility;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace ModularTurrets.Compat.CombatExtended;

[StaticConstructorOnStartup]
public static class CombatExtendedBoot
{
    public static bool Ready {get;private set;}
    public static string Failure {get;private set;}
    public static int MigratedVerbs,ConfiguredChannels,ShotSnapshots;
    public static object Snapshot()=>new {Ready,Failure,MigratedVerbs,ConfiguredChannels,ShotSnapshots,
        ceAssembly=typeof(Verb_ShootCE).Assembly.Location,compatAssembly=typeof(CombatExtendedBoot).Assembly.Location,
        provider=CombatCompatibility.Provider?.GetType().FullName,remote=CombatCompatibility.Remote?.GetType().FullName};
    public static object Inspect(Building_ModularTurret owner)=>new {owner.thingIDNumber,owner.Shells,owner.Powered,owner.HoldFire,
        bodyHeight=new CollisionVertical(owner).Max,legacyTransferred=owner.GetComp<CompMountedCE>()?.LegacyShellsTransferred,
        channels=owner.CombatChannels.Select(c=>new {c.Slot,gunId=c.Gun?.thingIDNumber,partId=c.Part?.thingIDNumber,
            verb=c.Verb?.GetType().FullName,c.Cooldown,c.Warmup,bursting=c.Verb?.Bursting,loadId=c.Verb?.loadID,
            shotHeight=(c.Verb as Verb_LaunchProjectileCE)?.ShotHeight,
            charges=c.Gun?.TryGetComp<CompCharges>()?.Props.chargeSpeeds?.ToArray(),
            ammo=c.Gun?.TryGetComp<CompAmmoUser>()?.CurrentAmmo?.defName,magazine=c.Gun?.TryGetComp<CompAmmoUser>()?.CurMagCount}).ToArray()};
    public static object InspectProjectile(Thing thing)
    {
        if(thing is not ProjectileCE projectile||!ModularTurrets.Compat.CombatExtended.ShotSnapshots.TryGet(projectile,out var record))return null;
        return new {projectile.thingIDNumber,type=projectile.GetType().FullName,damage=projectile.DamageAmount,penetration=projectile.PenetrationAmount,
            projectile.shotSpeed,projectile.shotAngle,projectile.shotRotation,projectile.shotHeight,
            savedDamage=record.Damage,record.ModularFactor,record.ArmorFactor,record.DamageQuality,record.ArmorQuality,
            firingFaction=record.Faction?.loadID,record.PreventFriendlyFire,record.Arrived};
    }
    static CombatExtendedBoot()
    {
        try
        {
            var harmony=new Harmony("local.modularturrets.compat.combatextended");
            var collision=AccessTools.Method(typeof(CollisionVertical),"CalculateHeightRange");
            if(collision==null)throw new MissingMethodException("CE CollisionVertical.CalculateHeightRange");
            harmony.Patch(collision,postfix:new HarmonyMethod(typeof(CombatExtendedBoot),nameof(CollisionHeight)));
            harmony.Patch(AccessTools.Method(typeof(ProjectileCE),nameof(ProjectileCE.Launch),new[]{typeof(Thing),typeof(Vector2),typeof(Thing)}),
                postfix:new HarmonyMethod(typeof(CombatExtendedBoot),nameof(ProjectileLaunched)));
            harmony.Patch(AccessTools.Method(typeof(CompAmmoUser),nameof(CompAmmoUser.TryStartReload)),
                prefix:new HarmonyMethod(typeof(CombatAdapter),nameof(CombatAdapter.RequestReload)));
            harmony.Patch(AccessTools.Method(typeof(global::CombatExtended.CombatExtended.Jobs.Utils.JobGiverUtils_Reload),
                    nameof(global::CombatExtended.CombatExtended.Jobs.Utils.JobGiverUtils_Reload.CanReload),
                    new[] { typeof(Pawn), typeof(Thing), typeof(bool), typeof(bool) }),
                prefix:new HarmonyMethod(typeof(CombatAdapter),nameof(CombatAdapter.AllowAutomaticMortarJob)));
            harmony.Patch(AccessTools.Method(typeof(VerbTracker),nameof(VerbTracker.ExposeData),Type.EmptyTypes),
                prefix:new HarmonyMethod(typeof(CombatAdapter),nameof(CombatAdapter.BeforeVerbLoad)),
                postfix:new HarmonyMethod(typeof(CombatAdapter),nameof(CombatAdapter.AfterVerbLoad)));
            ModularTurrets.Compat.CombatExtended.ShotSnapshots.Install(harmony);
            SpecialWeapons.Initialize(harmony);
            DesignatorCompatibility.Initialize(harmony);
            var adapter=new CombatAdapter();
            TurretRegistry.RegisterReloadableTurret(typeof(Building_ModularTurret),adapter.SetReloading,adapter.GetReloading,adapter.GetGun,adapter.GetAmmo);
            CombatCompatibility.Register(adapter,new RemoteMortarBackend());
            Ready=true;
        }
        catch(Exception error){Failure=error.ToString();Log.Error("[Modular Turrets CE] Adapter initialization failed; CE compatibility is unavailable. "+Failure);}
    }
    private static void CollisionHeight(Thing thing,ref FloatRange heightRange,ref float shotHeight)
    {
        if(thing is not Building_ModularTurret owner||owner.HeadPart==null)return;
        var extension=owner.def.GetModExtension<MountedHeightExtension>();
        heightRange=new FloatRange(heightRange.min,Math.Max(heightRange.max,extension?.bodyHeight ?? 2.5f));
        shotHeight=Math.Max(shotHeight,extension?.shotHeight ?? 2.6f);
    }
    private static void ProjectileLaunched(ProjectileCE __instance,Thing launcher,Thing equipment)
    {
        // Scale CE's own quality/kinetic-energy model once. Never add vanilla
        // armor penetration (different units), armor rolls or piercing impacts.
        ModularTurrets.Compat.CombatExtended.ShotSnapshots.Capture(__instance,launcher,equipment);
    }
}
