using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using CombatExtended;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace ModularTurrets.Compat.CombatExtended;

internal static class ShotSnapshots
{
    private static readonly ConditionalWeakTable<ProjectileCE,MountedShotSnapshot> records=new();
    private static readonly AccessTools.FieldRef<ProjectileCE,float?> RawDamage=AccessTools.FieldRefAccess<ProjectileCE,float?>("damageAmount");
    [ThreadStatic] private static ProjectileCE leaving;
    [ThreadStatic] internal static MountedShotSnapshot Arriving;
    [ThreadStatic] internal static MountedShotSnapshot WorldImpact;
    [ThreadStatic] private static MountedShotSnapshot impact;
    [ThreadStatic] private static ProjectileCE impactingProjectile;
    [ThreadStatic] private static MountedShotSnapshot secondary;
    [ThreadStatic] private static FragmentProtection fragmentProtection;
    [ThreadStatic] private static CompFragments fragmentEmitter;
    private struct FragmentEmissionState
    {
        internal CompFragments Previous;
        internal bool Entered;
    }
    private sealed class FragmentProtection
    {
        internal readonly Faction Faction;
        private readonly ThingDef projectile;
        private readonly Map map;
        private readonly Thing launcher;
        internal FragmentProtection(Faction faction,ThingDef projectile,Map map,Thing launcher)
        {Faction=faction;this.projectile=projectile;this.map=map;this.launcher=launcher;}
        internal bool Matches(ProjectileCE child,Thing source,Thing equipment)
            =>child.def==projectile&&child.Map==map&&source==launcher&&equipment==child;
    }
    private sealed class ProtectedFragments : IEnumerator,IDisposable
    {
        private readonly IEnumerator inner;
        private readonly FragmentProtection protection;
        internal ProtectedFragments(IEnumerator inner,FragmentProtection protection)
        {this.inner=inner;this.protection=protection;}
        public object Current=>inner.Current;
        public bool MoveNext()
        {
            var previous=fragmentProtection;fragmentProtection=protection;
            try{return inner.MoveNext();}finally{fragmentProtection=previous;}
        }
        public void Reset()=>inner.Reset();
        public void Dispose()
        {
            var previous=fragmentProtection;fragmentProtection=protection;
            try{(inner as IDisposable)?.Dispose();}finally{fragmentProtection=previous;}
        }
    }
    internal readonly struct SecondaryScope : IDisposable
    {
        private readonly MountedShotSnapshot previous;
        internal SecondaryScope(ProjectileCE source){previous=secondary;records.TryGetValue(source,out secondary);}
        public void Dispose()=>secondary=previous;
    }
    internal static SecondaryScope UseSecondary(ProjectileCE source)=>new(source);
    internal static bool IsProtected(Thing target,ProjectileCE source)
        =>records.TryGetValue(source,out var record)&&Protected(target,record);
    internal static bool TryGet(ProjectileCE source,out MountedShotSnapshot record)=>records.TryGetValue(source,out record);
    internal static bool IsMortarWeapon(ThingDef weapon)
    {
        if(weapon?.Verbs==null)return false;
        foreach(var verb in weapon.Verbs)
            if(verb.verbClass==typeof(Verb_MountedMortarCE))return true;
        return false;
    }
    internal static void Install(Harmony harmony)
    {
        harmony.Patch(AccessTools.Method(typeof(ProjectileCE),"CreateShellWorldObject"),
            prefix:new HarmonyMethod(typeof(ShotSnapshots),nameof(BeforeLeave)),finalizer:new HarmonyMethod(typeof(ShotSnapshots),nameof(AfterLeave)));
        harmony.Patch(AccessTools.Method(typeof(WorldObjectMaker),nameof(WorldObjectMaker.MakeWorldObject),new[]{typeof(WorldObjectDef)}),
            prefix:new HarmonyMethod(typeof(ShotSnapshots),nameof(ShellDefinition)),postfix:new HarmonyMethod(typeof(ShotSnapshots),nameof(ShellCreated)));
        harmony.Patch(AccessTools.Method(typeof(ProjectileCE),nameof(ProjectileCE.ExposeData)),postfix:new HarmonyMethod(typeof(ShotSnapshots),nameof(Expose)));
        harmony.Patch(AccessTools.PropertyGetter(typeof(ProjectileCE),nameof(ProjectileCE.PenetrationAmount)),postfix:new HarmonyMethod(typeof(ShotSnapshots),nameof(ArrivalPenetration)));
        harmony.Patch(AccessTools.Method(typeof(ProjectileCE),nameof(ProjectileCE.RayCast)),prefix:new HarmonyMethod(typeof(ShotSnapshots),nameof(RayCast)));
        harmony.Patch(AccessTools.Method(typeof(ProjectileCE),"CanCollideWith"),prefix:new HarmonyMethod(typeof(ShotSnapshots),nameof(CanCollide)));
        foreach(var type in new[]{typeof(ProjectileCE),typeof(BulletCE)})
            harmony.Patch(AccessTools.DeclaredMethod(type,nameof(ProjectileCE.Impact)),prefix:new HarmonyMethod(typeof(ShotSnapshots),nameof(BeforeImpact)),
                finalizer:new HarmonyMethod(typeof(ShotSnapshots),nameof(AfterImpact)));
        harmony.Patch(AccessTools.Method(typeof(GenExplosionCE),nameof(GenExplosionCE.DoExplosion)),prefix:new HarmonyMethod(typeof(ShotSnapshots),nameof(ExplosionIgnored)));
        harmony.Patch(AccessTools.Method(typeof(WorldObjectDamageWorker),nameof(WorldObjectDamageWorker.CalculateDamage)),postfix:new HarmonyMethod(typeof(ShotSnapshots),nameof(WorldDamage)));
        var fragments=AccessTools.DeclaredMethod(typeof(CompFragments),"FragRoutine",new[]{typeof(UnityEngine.Vector3),typeof(Map),typeof(float),
            typeof(Thing),typeof(ThingDefCountClass),typeof(float),typeof(float),typeof(FloatRange),typeof(FloatRange),typeof(float),typeof(bool)});
        var emission=AccessTools.DeclaredMethod(typeof(CompFragments),nameof(CompFragments.Throw),
            new[]{typeof(UnityEngine.Vector3),typeof(Map),typeof(Thing),typeof(float)});
        if(fragments?.IsStatic!=true||fragments.ReturnType!=typeof(IEnumerator)
            ||emission==null||emission.IsStatic||emission.ReturnType!=typeof(void))
        {WarnFragmentProtection("CompFragments.Throw/FragRoutine signature changed.");return;}
        var factoryPatch=AccessTools.Method(typeof(ShotSnapshots),nameof(ProtectFragmentRoutine));
        var before=AccessTools.Method(typeof(ShotSnapshots),nameof(BeforeFragmentEmission));
        var after=AccessTools.Method(typeof(ShotSnapshots),nameof(AfterFragmentEmission));
        try
        {
            harmony.Patch(emission,prefix:new HarmonyMethod(before),finalizer:new HarmonyMethod(after));
            harmony.Patch(fragments,postfix:new HarmonyMethod(factoryPatch));
        }
        catch(Exception error)
        {
            // Roll back only this pair; other CE behavior and patch owners remain.
            try{harmony.Unpatch(fragments,factoryPatch);harmony.Unpatch(emission,before);harmony.Unpatch(emission,after);}
            catch(Exception cleanup){WarnFragmentProtection(error.Message+" Cleanup: "+cleanup.Message);return;}
            WarnFragmentProtection(error.Message);
        }
    }
    private static void WarnFragmentProtection(string reason)
        =>Log.WarningOnce("[Modular Turrets CE] Mortar fragment building protection is unavailable. Keeping the original iterator. "+reason,
            GenText.StableStringHash("MTCEFragmentProtectionSignature"));
    private static void BeforeFragmentEmission(CompFragments __instance,out FragmentEmissionState __state)
    {
        __state=default;
        bool own=__instance.parent==impactingProjectile&&impact is {MortarShot:true,PreventFriendlyFire:true,Faction:not null};
        if(fragmentEmitter==null&&!own)return;
        __state=new FragmentEmissionState {Previous=fragmentEmitter,Entered=true};
        fragmentEmitter=own?__instance:null;
    }
    private static Exception AfterFragmentEmission(Exception __exception,FragmentEmissionState __state)
    {if(__state.Entered)fragmentEmitter=__state.Previous;return __exception;}
    private static void ProtectFragmentRoutine(ref IEnumerator __result,Map __1,Thing __3,ThingDefCountClass __4)
    {
        if(__result!=null&&__4?.thingDef!=null&&fragmentEmitter!=null&&fragmentEmitter.parent==impactingProjectile
            &&impact is {MortarShot:true,PreventFriendlyFire:true,Faction:not null} source)
            __result=new ProtectedFragments(__result,new FragmentProtection(source.Faction,__4.thingDef,__1,__3));
    }
    internal static void Capture(ProjectileCE projectile,Thing launcher,Thing equipment)
    {
        var protection=fragmentProtection;
        if(protection!=null&&!protection.Matches(projectile,launcher,equipment))protection=null;
        MountedShotSnapshot record;
        if(Arriving!=null)record=Arriving.Copy(true);
        else if(secondary!=null||impact!=null)
        {
            record=(secondary??impact).Copy(false);record.ArmorQuality=record.DamageQuality=1f;
            // Fragments retain pre-existing weapon upgrades. The specialist affects
            // the parent shell, independently of how CE schedules each fragment.
            if(record.MortarDamageFactor>0f)record.ModularFactor/=record.MortarDamageFactor;
            record.MortarDamageFactor=record.MortarRadiusFactor=1f;
            record.Damage=(RawDamage(projectile)??projectile.def.projectile.GetDamageAmount(1f,null))*record.ModularFactor;
        }
        else if(launcher is Building_ModularTurret owner)
        {
            float ordinary=RawDamage(projectile)??projectile.def.projectile.GetDamageAmount(equipment?.GetStatValue(StatDefOf.RangedWeapon_DamageMultiplier)??1f,null);
            float damageFactor=owner.ProjectileDamageMultiplier(equipment);
            float armorBonus=owner.ProjectileArmorBonus+0.05f*(owner.HeadPart?.TryGetComp<CompTurretPart>()?.Tier??0)+owner.ParagonOpeningArmorBonus;
            record=new MountedShotSnapshot {Damage=ordinary*damageFactor,ModularFactor=damageFactor,
                MortarDamageFactor=owner.MortarPayloadDamageFactor(equipment),
                MortarRadiusFactor=owner.MortarExplosionRadiusFactor(equipment),
                ArmorFactor=float.IsFinite(armorBonus)?Math.Max(0f,1f+armorBonus):1f,
                DamageQuality=equipment?.GetStatValue(StatDefOf.RangedWeapon_DamageMultiplier)??1f,
                ArmorQuality=equipment?.GetStatValue(StatDefOf.RangedWeapon_ArmorPenetrationMultiplier)??1f,
                Faction=owner.Faction,PreventFriendlyFire=owner.PreventFriendlyFire,MortarShot=IsMortarWeapon(equipment?.def)};
        }
        else if(protection!=null)
        {
            // No mounted launcher remains. Snapshot only this fragment's native
            // raw damage formula, never its parent's damage or upgrade factors.
            record=new MountedShotSnapshot {Damage=RawDamage(projectile)
                ??projectile.def.projectile.GetDamageAmount(equipment?.GetStatValue(StatDefOf.RangedWeapon_DamageMultiplier)??1f,null)};
        }
        else return;
        if(protection!=null)
        {
            record.Faction=protection.Faction;record.PreventFriendlyFire=true;record.MortarShot=true;
        }
        records.Remove(projectile);records.Add(projectile,record);projectile.DamageAmount=record.Damage;
        CombatExtendedBoot.ShotSnapshots++;
        if(record.Arrived&&!FloorCompatibility.TrySetFloor(projectile,0))throw new InvalidOperationException("CE shell arrival floor 0 unavailable");
    }
    private static void RayCast(ProjectileCE __instance,Thing launcher,Thing equipment)=>Capture(__instance,launcher,equipment);
    private static bool Protected(Thing thing,MountedShotSnapshot record)
        =>record?.PreventFriendlyFire==true&&record.Faction!=null
            &&(thing is Pawn pawn&&pawn.Faction!=null&&!pawn.InAggroMentalState&&!pawn.Faction.HostileTo(record.Faction)
                ||record.MortarShot&&thing is Building {Spawned:true}&&thing.Faction==record.Faction);
    private static bool CanCollide(ProjectileCE __instance,Thing thing,ref float dist,ref bool __result)
    {
        if(!records.TryGetValue(__instance,out var record)||!Protected(thing,record))return true;
        dist=-1f;__result=false;return false;
    }
    private struct ImpactState
    {
        internal MountedShotSnapshot Previous;
        internal ProjectileCE PreviousProjectile;
        internal IDisposable FloorScope;
        internal bool Entered;
    }
    private static bool BeforeImpact(ProjectileCE __instance,Thing hitThing,out ImpactState __state)
    {
        __state=new ImpactState {Previous=impact,PreviousProjectile=impactingProjectile,Entered=true};
        impactingProjectile=__instance;
        records.TryGetValue(__instance,out impact);
        // Arrival tagging outlives LaunchProjectile's scope. Impact and the
        // explosion it spawns must also run on ground, regardless of the view.
        if(impact?.Arrived==true&&!FloorCompatibility.TryEnter(__instance.Map,0,out __state.FloorScope))
            throw new InvalidOperationException("CE arrived shell impact floor-0 scope unavailable");
        return !Protected(hitThing,impact);
    }
    private static Exception AfterImpact(Exception __exception,ImpactState __state)
    {
        // A prior mod's false prefix can skip BeforeImpact entirely. Harmony
        // still invokes finalizers with the default state in that case.
        if(__state.Entered)
        {
            try{__state.FloorScope?.Dispose();}
            catch(Exception error){__exception??=error;}
            finally{impact=__state.Previous;impactingProjectile=__state.PreviousProjectile;}
        }
        return __exception;
    }
    private static void ExplosionIgnored(IntVec3 center,Map map,Thing instigator,ThingDef projectile,ref float radius,ref int damAmount,ref List<Thing> ignoredThings)
    {
        var shell=impactingProjectile;
        if(impact is { } snapshot && shell!=null && projectile==shell.def
            && (snapshot.MortarRadiusFactor!=1f || snapshot.MortarDamageFactor!=1f))
        {
            // CE's primary projectile explosion already reads its snapshotted DamageAmount.
            // Comp-only shells instead supply a fixed amount and pass the shell as instigator.
            if(instigator==shell.launcher && shell.def.projectile.explosionRadius>0f) radius*=snapshot.MortarRadiusFactor;
            else if(instigator==shell && shell.def.projectile.explosionRadius<=0f)
            {
                radius*=snapshot.MortarRadiusFactor;
                if(damAmount>0)damAmount=UnityEngine.Mathf.RoundToInt(damAmount*snapshot.MortarDamageFactor);
            }
        }
        if(impact?.PreventFriendlyFire!=true||map==null)return;
        ignoredThings??=new();foreach(Pawn pawn in map.mapPawns.AllPawnsSpawned)
            if(Protected(pawn,impact)&&!ignoredThings.Contains(pawn))ignoredThings.Add(pawn);
        if(impact.MortarShot&&shell!=null&&projectile==shell.def&&impact.Faction!=null)
        {
            CellRect? bounds=null;
            if(center.InBounds(map)&&float.IsFinite(radius)&&radius>=0f&&radius<Math.Max(map.Size.x,map.Size.z))
            {
                int reach=(int)Math.Ceiling(radius)+2;
                bounds=CellRect.FromLimits(center.x-reach,center.z-reach,center.x+reach,center.z+reach).ClipInsideMap(map);
            }
            var buildings=impact.Faction==Faction.OfPlayer?map.listerBuildings.allBuildingsColonist:map.listerBuildings.allBuildingsNonColonist;
            foreach(Building building in buildings)
                if(Protected(building,impact)&&(!bounds.HasValue||bounds.Value.Overlaps(building.OccupiedRect()))
                    &&!ignoredThings.Contains(building))ignoredThings.Add(building);
        }
    }
    private static void Expose(ProjectileCE __instance)
    {
        records.TryGetValue(__instance,out MountedShotSnapshot record);
        if(Scribe.mode==LoadSaveMode.Saving&&record==null)return;
        Scribe_Deep.Look(ref record,"mtCEShotSnapshot");
        if(record==null)return;
        records.Remove(__instance);records.Add(__instance,record);
        if(Scribe.mode==LoadSaveMode.PostLoadInit)
        {
            record.MortarShot|=IsMortarWeapon(__instance.equipmentDef);
            __instance.DamageAmount=record.Damage;
        }
    }
    private static void ArrivalPenetration(ProjectileCE __instance,ref float __result)
    {
        if(records.TryGetValue(__instance,out var record))__result*=record.ArmorFactor*(record.Arrived?record.ArmorQuality:1f);
    }
    private static void WorldDamage(ref float __result)
    {if(WorldImpact!=null)__result*=WorldImpact.ModularFactor*WorldImpact.DamageQuality;}
    private static void BeforeLeave(ProjectileCE __instance,out ProjectileCE __state){__state=leaving;leaving=__instance;}
    private static Exception AfterLeave(Exception __exception,ProjectileCE __state){leaving=__state;return __exception;}
    private static void ShellDefinition(ref WorldObjectDef __0)
    {
        if(leaving!=null&&__0==CE_WorldObjectDefOf.TravelingShell&&records.TryGetValue(leaving,out _))
            __0=DefDatabase<WorldObjectDef>.GetNamed("MT_CE_TravelingShell");
    }
    private static void ShellCreated(WorldObject __result)
    {
        if(__result is MountedTravelingShellCE shell&&leaving!=null&&records.TryGetValue(leaving,out var record))shell.Snapshot=record.Copy(false);
    }
}
