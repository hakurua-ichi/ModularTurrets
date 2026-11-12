using ModularTurrets.Compat.QuantumShields;
using Verse;

namespace ModularTurrets.Compat.Maru;

[StaticConstructorOnStartup]
internal static class MaruCompatibility
{
    static MaruCompatibility()
    {
        const string package = "VAMV.MaruRaceMod";
        if (!ModLister.AnyModActiveNoSuffix(new[] { package })) return;
        QuantumShieldAdapters.RegisterComp("local.modularturrets.maru", package, "MaruNegative.CompMaruShield");
    }
}
