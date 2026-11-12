using System;
using RimWorld;
using Verse;

namespace ModularTurrets.Compat.QuantumShields;

// Used only by adapters which must suppress a shield's stat factor. Primitive
// absorbers receive DamageInfo directly and do not need this wider scope.
internal static class QuantumDamageScope
{
    [ThreadStatic] private static Pawn victim;
    [ThreadStatic] private static bool changedStat;
    internal static Pawn Victim => victim;
    internal static void MarkStatOverride() => changedStat = true;
    internal struct State { internal Pawn Previous; internal bool PreviousChanged, Entered; }

    internal static void BeforeApply(DamageInfo __0, Pawn __1, bool __runOriginal, out State __state)
    {
        __state = default;
        if (!__runOriginal) return;
        Pawn next = QuantumDamage.Matches(__0) ? __1 : null;
        if (next == null && victim == null) return;
        __state = new State { Previous = victim, PreviousChanged = changedStat, Entered = true };
        // Prevent even a nested, unrelated cache-reading attack from observing
        // the outer quantum hit's temporary stat value.
        ClearChangedCache();
        victim = next;
        changedStat = false;
    }

    internal static Exception AfterApply(Exception __exception, State __state)
    {
        if (!__state.Entered) return __exception;
        try { ClearChangedCache(); }
        finally { victim = __state.Previous; changedStat = __state.PreviousChanged; }
        return __exception;
    }

    private static void ClearChangedCache()
    {
        if (changedStat && victim != null) StatDefOf.IncomingDamageFactor.Worker.ClearCacheForThing(victim);
    }
}
