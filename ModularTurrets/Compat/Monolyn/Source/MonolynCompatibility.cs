using ModularTurrets.Compat.QuantumShields;
using Verse;

namespace ModularTurrets.Compat.Monolyn;

[StaticConstructorOnStartup]
internal static class MonolynCompatibility
{
    static MonolynCompatibility()
    {
        const string package = "ASEL.MonolynRace";
        if (!ModLister.AnyModActiveNoSuffix(new[] { package })) return;
        QuantumShieldAdapters.RegisterApparel("local.modularturrets.monolyn", package, "ASEL.Shield");
    }
}
