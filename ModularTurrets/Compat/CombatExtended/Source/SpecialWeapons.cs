using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CombatExtended;
using CombatExtended.Lasers;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace ModularTurrets.Compat.CombatExtended;

internal static class SpecialWeapons
{
    [ThreadStatic] private static ProjectileCE active;
    [ThreadStatic] private static bool effectApplied;
    private static Action<Building_ModularTurret,float> showPulse;
    private static Action<Building_ModularTurret,Thing,Thing,DamageWorker.DamageResult> pulseHit;
    private static Action<Building_ModularTurret,Thing,Thing,DamageInfo,DamageWorker.DamageResult> disruptHit;
    private static Func<Building_ModularTurret,int> missileLane;
    internal static int LaserBeams,QuantumHits,Pulses,PulseHits,Missiles;

    internal static void Initialize(Harmony harmony)
    {
        // Reuse the owning mod's effects, without adding another main-assembly
        // dependency on CE or copying its injury/stun logic into this adapter.
        var assembly=typeof(Building_ModularTurret).Assembly;
        showPulse=Required(assembly.GetType("ModularTurrets.TeslaPulseVisuals"),"Spawn")
            .CreateDelegate<Action<Building_ModularTurret,float>>();
        pulseHit=Required(assembly.GetType("ModularTurrets.SpecialistHits"),"Pulse")
            .CreateDelegate<Action<Building_ModularTurret,Thing,Thing,DamageWorker.DamageResult>>();
        disruptHit=Required(assembly.GetType("ModularTurrets.SpecialistHits"),"Disrupt")
            .CreateDelegate<Action<Building_ModularTurret,Thing,Thing,DamageInfo,DamageWorker.DamageResult>>();
        missileLane=Required(typeof(Building_ModularTurret),"TakeMissileLane").CreateDelegate<Func<Building_ModularTurret,int>>();
        harmony.Patch(AccessTools.Method(typeof(Thing),nameof(Thing.TakeDamage),new[]{typeof(DamageInfo)}),
            postfix:new HarmonyMethod(typeof(SpecialWeapons),nameof(Damaged)));
    }
    private static MethodInfo Required(Type type,string name)
        =>type==null?throw new MissingMemberException("MT special-weapon effect owner unavailable: "+name)
            :AccessTools.DeclaredMethod(type,name)??throw new MissingMethodException(type.FullName,name);
    internal static void ShowPulse(Building_ModularTurret owner,float range){showPulse(owner,range);Pulses++;}
    internal static int NextLane(Building_ModularTurret owner)=>missileLane(owner);
    internal readonly struct ImpactScope : IDisposable
    {
        private readonly ProjectileCE previous;
        private readonly bool previousApplied;
        internal ImpactScope(ProjectileCE projectile){previous=active;previousApplied=effectApplied;active=projectile;effectApplied=false;}
        public void Dispose(){active=previous;effectApplied=previousApplied;}
    }
    private static void Damaged(Thing __instance,DamageInfo dinfo,DamageWorker.DamageResult __result)
    {
        var projectile=active;
        if(projectile==null||effectApplied||projectile.launcher is not Building_ModularTurret owner
            ||dinfo.Instigator!=owner||dinfo.Def!=projectile.DamageDef||ShotSnapshots.IsProtected(__instance,projectile))return;
        effectApplied=true;
        if(projectile is Projectile_MountedPulseHitCE)pulseHit(owner,projectile.equipment,__instance,__result);
        else disruptHit(owner,projectile.equipment,__instance,dinfo,__result);
    }
}
