using System;
using CombatExtended;
using HarmonyLib;
using Verse;

namespace ModularTurrets.Compat.CombatExtended;

// CE's binocular check recognizes only Building_TurretGunCE. Keep its radio
// rule, marker generation, cached interval and mortar accuracy calculation;
// add only the two owning-mod turret definitions to that availability check.
internal static class DesignatorCompatibility
{
    private static AccessTools.FieldRef<Verb_MarkForArtillery,int> lastChecked;
    private static AccessTools.FieldRef<Verb_MarkForArtillery,bool> conditionsMet;
    private static ThingDef legacyBase,largeBase;
    internal static int AdditionalChecks,EnabledByMountedMortar;

    internal static void Initialize(Harmony harmony)
    {
        lastChecked=AccessTools.FieldRefAccess<Verb_MarkForArtillery,int>("_lastConditionsCheckedAt");
        conditionsMet=AccessTools.FieldRefAccess<Verb_MarkForArtillery,bool>("_conditionsMet");
        legacyBase=DefDatabase<ThingDef>.GetNamed("MT_TurretBase");
        largeBase=DefDatabase<ThingDef>.GetNamed("MT_TurretBaseLarge");
        var method=AccessTools.DeclaredMethod(typeof(Verb_MarkForArtillery),nameof(Verb_MarkForArtillery.MarkingConditionsMet))
            ??throw new MissingMethodException(typeof(Verb_MarkForArtillery).FullName,nameof(Verb_MarkForArtillery.MarkingConditionsMet));
        harmony.Patch(method,prefix:new HarmonyMethod(typeof(DesignatorCompatibility),nameof(BeforeCheck)),
            postfix:new HarmonyMethod(typeof(DesignatorCompatibility),nameof(AfterCheck)));
    }

    private static void BeforeCheck(Verb_MarkForArtillery __instance,out int __state)=>__state=lastChecked(__instance);

    private static void AfterCheck(Verb_MarkForArtillery __instance,int __state,ref bool __result)
    {
        // A cached false must not create a new per-frame scan. CE's next actual
        // rare-interval recheck (or Dirty) also refreshes this additional result.
        if(__result||__state==lastChecked(__instance)||__instance.caster?.Spawned!=true||!__instance.CasterIsPawn)return;
        AdditionalChecks++;
        Thing caster=__instance.caster;
        if(!HasMountedMortar(caster,legacyBase)&&!HasMountedMortar(caster,largeBase))return;
        conditionsMet(__instance)=__result=true;EnabledByMountedMortar++;
    }

    private static bool HasMountedMortar(Thing caster,ThingDef def)
    {
        // O(1) empty-Def lookup on maps with no MT turrets; no AllThings scan,
        // new spawn registry, worker, or every-tick component is introduced.
        foreach(Thing thing in caster.Map.listerThings.ThingsOfDef(def))
            if(thing is Building_ModularTurret owner&&owner.Spawned&&!owner.Destroyed&&!owner.IsWrecked
                &&owner.Faction==caster.Faction&&owner.AttackVerb is Verb_MountedMortarCE)return true;
        return false;
    }
}
