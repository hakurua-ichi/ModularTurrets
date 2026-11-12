using System;
using System.Collections.Generic;
using HarmonyLib;
using ModularTurrets.Compat.QuantumShields;
using RimWorld;
using Verse;

namespace ModularTurrets.Compat.Wolfein;

[StaticConstructorOnStartup]
internal static class WolfeinCompatibility
{
    private const string Package = "MelonDove.WolfeinRace", Owner = "local.modularturrets.wolfein";

    static WolfeinCompatibility()
    {
        if (!ModLister.AnyModActiveNoSuffix(new[] { Package })) return;
        var harmony = new Harmony(Owner);
        System.Reflection.MethodInfo apply = null, factor = null;
        try
        {
            var def = DefDatabase<HediffDef>.GetNamedSilentFail("Wolfein_PrototypeShieldBelt");
            if (!QuantumDamage.From(def, Package) || def.stages == null)
                throw new InvalidOperationException("Wolfein_PrototypeShieldBelt");
            PrototypeShield.RegisterStages(def.stages);
            if (!PrototypeShield.HasStages) throw new InvalidOperationException("Prototype shield stages");
            apply = AccessTools.DeclaredMethod(typeof(DamageWorker_AddInjury), "ApplyToPawn", new[] { typeof(DamageInfo), typeof(Pawn) });
            factor = AccessTools.DeclaredMethod(typeof(HediffStatsUtility), nameof(HediffStatsUtility.GetStatFactorForSeverity),
                new[] { typeof(StatDef), typeof(HediffStage), typeof(Pawn), typeof(float) });
            if (apply?.ReturnType != typeof(DamageWorker.DamageResult) || factor?.ReturnType != typeof(float))
                throw new MissingMethodException("Quantum prototype-shield damage/stat scope");
            harmony.Patch(apply,
                prefix: new HarmonyMethod(typeof(QuantumDamageScope), nameof(QuantumDamageScope.BeforeApply)) { priority = Priority.Last },
                finalizer: new HarmonyMethod(typeof(QuantumDamageScope), nameof(QuantumDamageScope.AfterApply)));
            harmony.Patch(factor, prefix: new HarmonyMethod(typeof(PrototypeShield), nameof(PrototypeShield.BeforeFactor)) { priority = Priority.Last });
        }
        catch (Exception error)
        {
            if (apply != null) harmony.Unpatch(apply, HarmonyPatchType.All, Owner);
            if (factor != null) harmony.Unpatch(factor, HarmonyPatchType.All, Owner);
            QuantumShieldAdapters.Warn(Package, error);
        }
    }
}
