using System;
using HarmonyLib;
using Verse;
namespace ModularTurrets.Compat.Milira;
[StaticConstructorOnStartup]
internal static class MiliraCompatibility
{
    static MiliraCompatibility()
    {
        if (!ModLister.AnyModActiveNoSuffix(new[] { QuantumShieldBypass.Package })) return;
        try
        {
            var type = AccessTools.TypeByName("AncotLibrary.CompPhysicalShield");
            var method = type == null ? null : AccessTools.DeclaredMethod(type, "PostPreApplyDamage",
                new[] { typeof(DamageInfo).MakeByRefType(), typeof(bool).MakeByRefType() });
            if (method?.ReturnType != typeof(void) || !typeof(ThingComp).IsAssignableFrom(type))
                throw new MissingMethodException("AncotLibrary.CompPhysicalShield.PostPreApplyDamage");
            new Harmony("local.modularturrets.milira").Patch(method,
                prefix: new HarmonyMethod(typeof(QuantumShieldBypass), nameof(QuantumShieldBypass.BeforeShield)) { priority = Priority.Last });
        }
        catch (Exception error) { Log.Warning(L.T("MT_MiliraShieldCompatibilityFailed", error.Message)); }
    }
}
