using System;
using System.Collections.Generic;
using HarmonyLib;
using ModularTurrets.Compat.QuantumShields;
using RimWorld;
using Verse;

namespace ModularTurrets.Compat.Wolfein;

internal static class PrototypeShield
{
    private static readonly HashSet<HediffStage> stages = new(ReferenceEqualityComparer.Instance);
    internal static bool HasStages => stages.Count > 0;
    internal static void RegisterStages(IEnumerable<HediffStage> source)
    {
        foreach (var stage in source)
            if (stage?.statFactors?.Exists(stat => stat.stat == StatDefOf.IncomingDamageFactor && stat.value < 1f) == true)
                stages.Add(stage);
    }

    internal static bool BeforeFactor(StatDef __0, HediffStage __1, Pawn __2, ref float __result, bool __runOriginal)
    {
        if (!__runOriginal || __2 == null || !ReferenceEquals(QuantumDamageScope.Victim, __2)
            || __0 != StatDefOf.IncomingDamageFactor || !stages.Contains(__1)) return true;
        __result = 1f;
        QuantumDamageScope.MarkStatOverride();
        return false;
    }
}
