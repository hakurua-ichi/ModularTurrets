using ModularTurrets.Compat.QuantumShields;
using Verse;
namespace ModularTurrets.Compat.Milira;
internal static class QuantumShieldBypass
{
    internal const string Package = "Ancot.MiliraRace";
    internal static bool IsQuantum(DamageInfo damage) => QuantumDamage.Matches(damage);
    internal static bool IsMilira(Def def) => QuantumDamage.From(def, Package);
    internal static bool BeforeShield(ThingComp __instance, DamageInfo __0, ref bool __1, bool __runOriginal)
    {
        if (!__runOriginal || !IsQuantum(__0) || !IsMilira(__instance?.parent?.def)) return true;
        __1 = false;
        return false;
    }
}
