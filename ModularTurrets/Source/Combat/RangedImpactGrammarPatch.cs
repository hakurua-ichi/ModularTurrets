using System;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Verse;
using Verse.Grammar;

namespace ModularTurrets;

[StaticConstructorOnStartup]
internal static class RangedImpactGrammarPatch
{
    private const string OwnerId = "local.modularturrets.orphaned-impact-grammar";
    private static bool initialized;

    static RangedImpactGrammarPatch() => Initialize();

    internal static void Initialize()
    {
        if (initialized) return;
        try
        {
            ApplyPatch();
            initialized = true;
        }
        catch (Exception exception)
        {
            ModDiagnostics.ErrorOnce("Combat.ImpactGrammarPatch", exception);
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ApplyPatch()
    {
        var original = AccessTools.DeclaredMethod(typeof(BattleLogEntry_RangedImpact), "GenerateGrammarRequest");
        if (original == null || original.ReturnType != typeof(GrammarRequest))
            throw new MissingMethodException(typeof(BattleLogEntry_RangedImpact).FullName, "GenerateGrammarRequest()");
        new Harmony(OwnerId).Patch(original, postfix: new HarmonyMethod(typeof(RangedImpactGrammarPatch), nameof(GrammarPostfix))
        {
            priority = Priority.Last
        });
    }

    private static void GrammarPostfix(ThingDef ___projectileDef, ref GrammarRequest __result)
    {
        if (___projectileDef?.thingClass != typeof(Projectile_ModularBullet)
            || !__result.Constants.TryGetValue("INITIATOR_missing", out string missing) || missing != "True") return;

        // Core records a missing initiator but its ranged rules still require a name.
        // Supply display-only fallbacks; keep the log, damage and launcher unchanged.
        if (!__result.HasRule("INITIATOR_definite"))
            __result.Rules.Add(new Rule_String("INITIATOR_definite", L.T("MT_UnknownLauncher")));
        if (!__result.HasRule("INITIATOR_possessive"))
            __result.Rules.Add(new Rule_String("INITIATOR_possessive", L.T("MT_UnknownLauncherPossessive")));
    }
}
