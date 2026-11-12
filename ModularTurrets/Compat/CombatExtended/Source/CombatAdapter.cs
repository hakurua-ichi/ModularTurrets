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

internal sealed partial class CombatAdapter : ICombatCompatibility, IMortarAutoloadBackend, IMortarAmmoManagementBackend
{
    private static readonly AccessTools.FieldRef<Verb,int> Remaining=AccessTools.FieldRefAccess<Verb,int>("burstShotsLeft");
    private static readonly AccessTools.FieldRef<Verb,int> NextShot=AccessTools.FieldRefAccess<Verb,int>("ticksToNextBurstShot");
    private static readonly AccessTools.FieldRef<Verb,LocalTargetInfo> Target=AccessTools.FieldRefAccess<Verb,LocalTargetInfo>("currentTarget");
    private static readonly AccessTools.FieldRef<Verb,LocalTargetInfo> Destination=AccessTools.FieldRefAccess<Verb,LocalTargetInfo>("currentDestination");
    private static readonly AccessTools.FieldRef<Verb,int> LastShot=AccessTools.FieldRefAccess<Verb,int>("lastShotTick");
    private static readonly AccessTools.FieldRef<Verb,bool> Surprise=AccessTools.FieldRefAccess<Verb,bool>("surpriseAttack");
    private static readonly AccessTools.FieldRef<Verb,bool> NonTarget=AccessTools.FieldRefAccess<Verb,bool>("canHitNonTargetPawnsNow");
    private static readonly AccessTools.FieldRef<Verb,bool> NonInterrupting=AccessTools.FieldRefAccess<Verb,bool>("nonInterruptingSelfCast");
    private static readonly AccessTools.FieldRef<VerbTracker,List<Verb>> TrackedVerbs=AccessTools.FieldRefAccess<VerbTracker,List<Verb>>("verbs");
    private static readonly Func<Building_Turret,bool> Stunned=AccessTools.PropertyGetter(typeof(Building_Turret),"IsStunned").CreateDelegate<Func<Building_Turret,bool>>();
    internal static bool EnsureAmmoSelection(CompAmmoUser ammo,out string reason)
    {
        AmmoDef first=null;
        if(ammo.CurAmmoSet?.ammoTypes is { } links)
            foreach(var link in links)
                if(link?.ammo!=null&&link.projectile!=null){first=link.ammo;break;}
        if(first==null)
        {
            reason=L.T("MT_CEAmmoSelectionMissing",L.Label(ammo.parent.def));
            Log.ErrorOnce("[Modular Turrets CE] "+reason,GenText.StableStringHash("MTCEAmmoSet:"+ammo.parent.def.defName));
            return false;
        }
        // Match CE Initialize's reference defaults after old saves have read
        // missing ammo fields as null. Never refill/reset a magazine here.
        if(ammo.CurrentAmmo==null)ammo.CurrentAmmo=first;
        if(ammo.SelectedAmmo==null)ammo.SelectedAmmo=ammo.CurrentAmmo??first;
        reason=null;return true;
    }
    private static bool MountedDefinition(CompEquippable equippable)
        =>equippable.parent?.def?.Verbs?.Any(p=>p.verbClass.Assembly==typeof(CombatAdapter).Assembly
            && typeof(Verb_LaunchProjectileCE).IsAssignableFrom(p.verbClass))==true;
    internal static void BeforeVerbLoad(VerbTracker __instance,out Verb[] __state)
    {
        __state=null;
        if(Scribe.mode!=LoadSaveMode.ResolvingCrossRefs||__instance.directOwner is not CompEquippable equippable
            || !MountedDefinition(equippable))return;
        // Core rebuilds by exact type+loadID here, before the turret's PostLoadInit.
        // Keep loaded objects until their references resolve, then transfer progress.
        __state=TrackedVerbs(__instance)?.ToArray();
    }
    internal static void AfterVerbLoad(VerbTracker __instance,Verb[] __state)
    {
        if(__state!=null)TransferVerbState(__state,__instance.AllVerbs,null);
    }
    private static void TransferVerbState(Verb[] previous,List<Verb> updated,Thing caster)
    {
        for(int i=0;i<updated.Count;i++)
        {
            Verb next=updated[i];
            if(next is not Verb_LaunchProjectileCE)continue;
            Verb old=previous.FirstOrDefault(v=>v!=null&&v.loadID==next.loadID);
            if(old==null&&i<previous.Length&&string.IsNullOrEmpty(previous[i]?.loadID))old=previous[i];
            if(old==null||ReferenceEquals(old,next))continue;
            next.loadID=old.loadID??next.loadID;next.state=old.state;Target(next)=Target(old);Destination(next)=Destination(old);
            Remaining(next)=Remaining(old);NextShot(next)=NextShot(old);LastShot(next)=LastShot(old);
            Surprise(next)=Surprise(old);NonTarget(next)=NonTarget(old);NonInterrupting(next)=NonInterrupting(old);
            next.preventFriendlyFire=old.preventFriendlyFire;next.caster=caster??old.caster;
            next.castCompleteCallback=old.castCompleteCallback;CombatExtendedBoot.MigratedVerbs++;
        }
    }
    public void PrepareChannel(Building_ModularTurret owner,FireChannel channel)
    {
        if(channel.Gun is not ThingWithComps gun)return;
        // Loaded pre-CE guns retain their object/part IDs and all existing comps.
        // Append only missing CE comps, rather than reinitialize the whole gun.
        foreach(var props in gun.def.comps)
        {
            if(props.compClass.Namespace?.StartsWith("CombatExtended",StringComparison.Ordinal)!=true
                || gun.AllComps.Any(c=>props.compClass.IsInstanceOfType(c)))continue;
            var comp=(ThingComp)Activator.CreateInstance(props.compClass);comp.parent=gun;
            if(comp is CompAmmoUser ammo)ammo.turret=owner;
            gun.AllComps.Add(comp);comp.Initialize(props);
        }
        if(gun.TryGetComp<CompAmmoUser>() is { } preparedAmmo)EnsureAmmoSelection(preparedAmmo,out _);
        var equippable=gun.TryGetComp<CompEquippable>();if(equippable==null)return;
        var previous=equippable.AllVerbs.ToArray();var expected=gun.def.Verbs;
        bool mismatch=previous.Length<expected.Count;
        for(int i=0;i<Math.Min(previous.Length,expected.Count);i++)mismatch|=previous[i].GetType()!=expected[i].verbClass;
        if(!mismatch)return;
        equippable.verbTracker.InitVerbsFromZero();
        TransferVerbState(previous,equippable.AllVerbs,owner);
    }
    public void ConfigureChannel(Building_ModularTurret owner,FireChannel channel)
    {
        if(channel.Verb is not Verb_LaunchProjectileCE)throw new InvalidOperationException("CE-active modular weapon was not converted: "+channel.Gun?.def.defName);
        if(channel.Verb is Verb_MountedMortarCE&&channel.Gun.TryGetComp<CompCharges>()?.Props?.chargeSpeeds is not {Count:>0})
            throw new InvalidOperationException("CE modular mortar has no ballistic charges: "+channel.Gun?.def.defName);
        if(channel.Gun.TryGetComp<CompAmmoUser>() is { } ammo){ammo.turret=owner;EnsureAmmoSelection(ammo,out _);}
        CombatExtendedBoot.ConfiguredChannels++;
        TransferLegacy(owner,channel);
    }
    public void TickChannel(Building_ModularTurret owner,FireChannel channel)
    {
        if(channel.Verb is not Verb_LaunchProjectileCE ce){channel.Verb?.Reset();channel.Cooldown=Math.Max(channel.Cooldown,2);return;}
        TransferLegacy(owner,channel);
        var ammo=channel.Gun.TryGetComp<CompAmmoUser>();
        if(ammo!=null&&(ammo.CurAmmoSet?.ammoTypes is not {Count:>0}||ammo.SelectedAmmo==null))
        {channel.CompatibilityBlocked=true;return;}
        var reload=owner.GetComp<CompMountedCE>();
        if(ammo is {CanBeFiredNow:false}||reload is {Reloading:true}&&reload.ReloadGun==channel.Gun)
        {
            channel.CompatibilityBlocked=true;
            if(ammo!=null && (owner.AutoReload || ce is not Verb_MountedMortarCE))
            {RequestReload(ammo);ammo.TryStartReload();}
            return;
        }
        if(!owner.Powered||owner.HoldFire||Stunned(owner))return;
        ce.VerbTickCE();
        if(ce is IMountedAiming aiming&&aiming.TickMountedAim())channel.CompatibilityBlocked=true;
    }
    internal static void RequestReload(CompAmmoUser __instance)
    {
        if(__instance.turret is not Building_ModularTurret owner)return;
        var state=owner.GetComp<CompMountedCE>();
        if(state!=null&&!state.Reloading)state.ReloadGun=__instance.parent;
    }
    internal Thing GetGun(Building_Turret turret)
    {
        var owner=(Building_ModularTurret)turret;var state=owner.GetComp<CompMountedCE>();
        if(state?.ReloadGun!=null&&owner.CombatChannels.Any(c=>c.Gun==state.ReloadGun))return state.ReloadGun;
        return owner.CombatChannels.FirstOrDefault(c=>c.Slot=="head")?.Gun;
    }
    internal CompAmmoUser GetAmmo(Building_Turret turret)=>GetGun(turret)?.TryGetComp<CompAmmoUser>();
    internal bool GetReloading(Building_Turret turret)=>((Building_ModularTurret)turret).GetComp<CompMountedCE>()?.Reloading==true;
    internal void SetReloading(Building_Turret turret,bool value)
    {var state=((Building_ModularTurret)turret).GetComp<CompMountedCE>();if(state!=null){state.Reloading=value;if(!value)state.ReloadGun=null;}}

    private static void TransferLegacy(Building_ModularTurret owner,FireChannel channel)
    {
        if(channel.Slot!="head"||channel.Verb is not Verb_MountedMortarCE||channel.Gun.TryGetComp<CompAmmoUser>() is not { } ammo)return;
        var state=owner.GetComp<CompMountedCE>();if(state==null)throw new InvalidOperationException("Missing CE migration state component");
        if(state.InitialMagazineTransferred && owner.Shells<=0)return;
        var shell=owner.LoadedShellDef ?? DefDatabase<ThingDef>.GetNamedSilentFail("Shell_HighExplosive");
        if(shell==null)return;
        if(!state.InitialMagazineTransferred)
        {
            state.InitialMagazineTransferred=true;
            if(owner.Shells>0 && ammo.CurMagCount==0 && ammo.UseAmmo
                && shell is AmmoDef { ammoCount:1 } actual && AllowedAmmo(ammo,actual))
            {
                int amount=Math.Min(owner.Shells,ammo.MagSize);ammo.CurrentAmmo=actual;ammo.SelectedAmmo=actual;ammo.CurMagCount=amount;
                owner.Shells-=amount;state.LegacyShellsTransferred+=amount;
            }
            if(owner.SelectedShellDef is AmmoDef chosen && AllowedAmmo(ammo,chosen))ammo.SelectedAmmo=chosen;
        }
        if(owner.Shells<=0){owner.LoadedShellDef=null;return;}
        // Excess/unconvertible Core rounds leave as their original physical item.
        if(!owner.Spawned || !FloorCompatibility.TryGetFloor(owner,out int floor)
            || !FloorCompatibility.TryEnter(owner.Map,floor,out var scope))return;
        using(scope)
        {
            int count=Math.Min(owner.Shells,shell.stackLimit);Thing stack=ThingMaker.MakeThing(shell);stack.stackCount=count;
            int delivered=0;
            if(FloorCompatibility.TrySetFloor(stack,floor))
                GenPlace.TryPlaceThing(stack,owner.Position,owner.Map,ThingPlaceMode.Near,(_,n)=>delivered+=n);
            if(!stack.Spawned&&!stack.Destroyed)stack.Destroy();
            owner.Shells-=delivered;state.LegacyShellsTransferred+=delivered;
            if(owner.Shells==0)owner.LoadedShellDef=null;
        }
    }
}
