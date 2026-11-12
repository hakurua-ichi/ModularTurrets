using System;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace ModularTurrets;

[StaticConstructorOnStartup]
internal static class RelocationPatch
{
    internal static bool Ready { get; private set; }

    static RelocationPatch()
    {
        try
        {
            var make = AccessTools.DeclaredMethod(typeof(MinifyUtility), nameof(MinifyUtility.MakeMinified),
                new[] { typeof(Thing), typeof(DestroyMode) });
            var uninstall = AccessTools.DeclaredMethod(typeof(MinifyUtility), nameof(MinifyUtility.Uninstall), new[] { typeof(Thing) });
            var toil = AccessTools.DeclaredMethod(typeof(Toils_Construct), nameof(Toils_Construct.UninstallIfMinifiable), new[] { typeof(TargetIndex) });
            var designate = AccessTools.DeclaredMethod(typeof(Designator_Uninstall), nameof(Designator_Uninstall.CanDesignateThing), new[] { typeof(Thing) });
            if (make?.ReturnType != typeof(MinifiedThing) || uninstall?.ReturnType != typeof(MinifiedThing)
                || toil?.ReturnType != typeof(Toil) || designate?.ReturnType != typeof(AcceptanceReport))
                throw new MissingMethodException("Native minify/reinstall signatures changed.");
            var harmony = new Harmony("local.modularturrets.empty-base-relocation");
            var guard = new HarmonyMethod(typeof(RelocationPatch), nameof(GuardMinify));
            harmony.Patch(make, prefix: guard);
            harmony.Patch(uninstall, prefix: guard);
            harmony.Patch(toil, postfix: new HarmonyMethod(typeof(RelocationPatch), nameof(GuardUninstallToil)));
            harmony.Patch(designate, prefix: new HarmonyMethod(typeof(RelocationPatch), nameof(GuardUninstallDesignation)));
            Ready = true;
        }
        catch (Exception error)
        {
            // Failed protection must not expose a vanilla bypass through the shared building def.
            foreach (var def in DefDatabase<ThingDef>.AllDefsListForReading)
                if (def.thingClass != null && typeof(Building_ModularTurret).IsAssignableFrom(def.thingClass)) def.minifiedDef = null;
            ModDiagnostics.WarningOnce("Construction.RelocationPatch", error);
        }
    }

    private static bool GuardMinify(Thing __0, ref MinifiedThing __result)
    {
        if (__0 is not Building_ModularTurret turret || turret.CanMinifyForRelocation(out string reason)) return true;
        __result = null;
        Messages.Message(reason, turret, MessageTypeDefOf.RejectInput, false);
        return false;
    }

    private static void GuardUninstallToil(TargetIndex __0, Toil __result)
    {
        var toil = __result;
        bool Blocked() => toil.actor?.CurJob?.GetTarget(__0).Thing is Building_ModularTurret turret
            && !turret.CanMinifyForRelocation(out _);
        toil.FailOn(Blocked);
        // Native interval work does not recheck end conditions. Stop before its MakeMinified call.
        toil.AddPreTickIntervalAction(_ =>
        {
            if (Blocked()) toil.actor.jobs.curDriver.EndJobWith(JobCondition.Incompletable);
        });
    }

    private static bool GuardUninstallDesignation(Thing __0, ref AcceptanceReport __result)
    {
        if (__0 is not Building_ModularTurret turret || turret.CanMinifyForRelocation(out string reason)) return true;
        __result = new AcceptanceReport(reason);
        return false;
    }
}
