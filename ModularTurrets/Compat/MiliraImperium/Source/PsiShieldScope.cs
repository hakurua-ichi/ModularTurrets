using System;
using System.Collections;
using System.Reflection;
using Verse;

namespace ModularTurrets.Compat.MiliraImperium;

internal static class PsiShieldScope
{
    [ThreadStatic] private static Pawn victim;
    internal static FieldInfo Registry;
    internal struct State { internal Pawn Previous; internal bool Entered; }

    private static ThingComp Shield(Pawn pawn)
        => pawn != null && Registry?.GetValue(null) is IDictionary shields
            && shields[pawn] is ThingComp shield && QuantumShieldBypass.IsMilira(shield.parent?.def) ? shield : null;

    private static void Enter(Pawn pawn, DamageInfo? damage, bool run, out State state)
    {
        state = default;
        if (!run) return;
        Pawn next = damage.HasValue && QuantumShieldBypass.IsQuantum(damage.Value) && Shield(pawn) != null ? pawn : null;
        if (next == null && victim == null) return;
        state = new State { Previous = victim, Entered = true };
        // Clear an outer quantum context during nested unrelated damage.
        victim = next;
    }

    internal static void BeforeState(DamageInfo? __0, Pawn ___pawn, bool __runOriginal, out State __state)
        => Enter(___pawn, __0, __runOriginal, out __state);

    internal static void BeforePostDamage(DamageInfo __0, Pawn ___pawn, bool __runOriginal, out State __state)
        => Enter(___pawn, __0, __runOriginal, out __state);

    internal static Exception AfterState(Exception __exception, State __state)
    {
        if (__state.Entered) victim = __state.Previous;
        return __exception;
    }

    // Preserve Core's death result; skip only Imperium's Psi shield postfix.
    internal static bool BeforeDeathPostfix(Pawn __0)
        => victim != __0 || __0 == null || Shield(__0) == null;

    internal static bool BeforeIntercept(ThingComp __instance, ref bool __result, bool __runOriginal)
    {
        if (!__runOriginal || victim == null || !ReferenceEquals(Shield(victim), __instance)) return true;
        __result = false;
        return false;
    }
}
