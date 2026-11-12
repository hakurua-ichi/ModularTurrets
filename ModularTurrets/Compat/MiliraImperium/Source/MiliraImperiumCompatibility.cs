using System;
using System.Collections;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace ModularTurrets.Compat.MiliraImperium;

[StaticConstructorOnStartup]
internal static class MiliraImperiumCompatibility
{
    private const string Owner = "local.modularturrets.milira-imperium";
    private static readonly Type[] DamageArguments = { typeof(DamageInfo).MakeByRefType(), typeof(bool).MakeByRefType() };

    static MiliraImperiumCompatibility()
    {
        if (!ModLister.AnyModActiveNoSuffix(new[] { QuantumShieldBypass.Package })) return;
        var harmony = new Harmony(Owner);
        TryFeature(() => PatchComp(harmony, "AncotLibrary.CompPhysicalShield"), "physical shield");
        TryFeature(() => PatchComp(harmony, "MiliraImperium.Comp_DeflectorShield"), "Imperium deflector");
        TryFeature(() =>
        {
            var method = RequireMethod(RequireType("PLAMiliraSariel.HediffWithComps_HardenedShield"),
                "Notify_Pawn_PreApplyDamage", typeof(bool), typeof(DamageInfo));
            harmony.Patch(method, prefix: Hook(typeof(QuantumShieldBypass), nameof(QuantumShieldBypass.BeforeHardenedShield)));
        }, "Imperium hardened shield");
        TryFeature(() => PatchPsiShield(harmony), "Imperium Psi shield");
    }

    private static void PatchPsiShield(Harmony harmony)
    {
        var registry = AccessTools.Field(RequireType("MiliraImperium.InterceptKillRegistry"), "activeShields");
        if (registry == null || !registry.IsStatic || !typeof(IDictionary).IsAssignableFrom(registry.FieldType))
            throw new MissingFieldException("InterceptKillRegistry.activeShields");
        var death = RequireMethod(RequireType("MiliraImperium.MiliraImperium_Pawn_HealthTracker_Patch"),
            "Patch_ShouldBeDead", typeof(void), typeof(Pawn), typeof(bool).MakeByRefType());
        var intercept = RequireMethod(RequireType("MiliraImperium.Comp_DeflectorShield"), "TryInterceptKill", typeof(bool));
        var state = RequireMethod(typeof(Pawn_HealthTracker), "CheckForStateChange", typeof(void), typeof(DamageInfo?), typeof(Hediff));
        var post = RequireMethod(typeof(Pawn_HealthTracker), "PostApplyDamage", typeof(void), typeof(DamageInfo), typeof(float));
        PsiShieldScope.Registry = registry;
        try
        {
            harmony.Patch(state, prefix: Hook(typeof(PsiShieldScope), nameof(PsiShieldScope.BeforeState)),
                finalizer: Hook(typeof(PsiShieldScope), nameof(PsiShieldScope.AfterState)));
            harmony.Patch(post, prefix: Hook(typeof(PsiShieldScope), nameof(PsiShieldScope.BeforePostDamage)),
                finalizer: Hook(typeof(PsiShieldScope), nameof(PsiShieldScope.AfterState)));
            harmony.Patch(death, prefix: Hook(typeof(PsiShieldScope), nameof(PsiShieldScope.BeforeDeathPostfix)));
            harmony.Patch(intercept, prefix: Hook(typeof(PsiShieldScope), nameof(PsiShieldScope.BeforeIntercept)));
        }
        catch
        {
            foreach (var method in new[] { state, post, death, intercept }) harmony.Unpatch(method, HarmonyPatchType.All, Owner);
            PsiShieldScope.Registry = null;
            throw;
        }
    }

    private static void PatchComp(Harmony harmony, string name) => PatchComp(harmony, RequireType(name));
    private static void PatchComp(Harmony harmony, Type type)
    {
        if (!typeof(ThingComp).IsAssignableFrom(type)) throw new InvalidOperationException(type.FullName);
        harmony.Patch(RequireMethod(type, "PostPreApplyDamage", typeof(void), DamageArguments),
            prefix: Hook(typeof(QuantumShieldBypass), nameof(QuantumShieldBypass.BeforeShield)));
    }
    private static Type RequireType(string name) => AccessTools.TypeByName(name) ?? throw new TypeLoadException(name);
    private static MethodInfo RequireMethod(Type type, string name, Type result, params Type[] arguments)
    {
        var method = AccessTools.DeclaredMethod(type, name, arguments);
        return method?.ReturnType == result ? method : throw new MissingMethodException(type.FullName, name);
    }
    private static HarmonyMethod Hook(Type type, string name) => new(type, name) { priority = Priority.Last };
    private static void TryFeature(Action apply, string feature)
    {
        try { apply(); }
        catch (Exception error) { Log.Warning(L.T("MT_MiliraImperiumShieldCompatibilityFailed", feature, error.Message)); }
    }
}
