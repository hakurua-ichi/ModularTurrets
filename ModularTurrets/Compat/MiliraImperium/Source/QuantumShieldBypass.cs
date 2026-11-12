using ModularTurrets.Compat.QuantumShields;
using Verse;
namespace ModularTurrets.Compat.MiliraImperium;
internal static class QuantumShieldBypass
{
    internal const string Package = "Ariandel.MiliraImperium";
    internal static bool IsQuantum(DamageInfo damage) => QuantumDamage.Matches(damage);
    internal static bool IsMilira(Def def) => QuantumDamage.From(def, Package);
    internal static bool BeforeShield(ThingComp __instance, DamageInfo __0, ref bool __1, bool __runOriginal)
    {
        if (!__runOriginal || !IsQuantum(__0) || !IsMilira(__instance?.parent?.def)) return true;
        __1 = false;
        return false;
    }
    internal static bool BeforeHardenedShield(HediffWithComps __instance, DamageInfo __0, ref bool __result, bool __runOriginal)
    {
        if (!__runOriginal || !IsQuantum(__0) || !IsMilira(__instance?.def)) return true;
        __result = false;
        return false;
    }
}
