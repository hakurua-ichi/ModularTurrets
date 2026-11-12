using System;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Verse;

namespace ModularTurrets;

[StaticConstructorOnStartup]
internal static class TurretDamageOverlayLayer
{
    static TurretDamageOverlayLayer()
    {
        try { ApplyPatch(); }
        catch (Exception exception)
        {
            Log.Warning("[Modular Turrets] Base damage overlay layer unavailable: " + exception.Message);
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ApplyPatch()
    {
        var original = AccessTools.DeclaredMethod(typeof(SectionLayer_BuildingsDamage),
            "GetDamageTexturesAltitude", new[] { typeof(Building) });
        if (original == null || original.ReturnType != typeof(float))
            throw new MissingMethodException(nameof(SectionLayer_BuildingsDamage), "GetDamageTexturesAltitude(Building)");
        new Harmony("local.modularturrets.base-damage-overlay-layer").Patch(original,
            postfix: new HarmonyMethod(typeof(TurretDamageOverlayLayer), nameof(KeepOnBase)));
    }

    private static void KeepOnBase(Building __0, ref float __result)
    {
        // Core prints at +5 AltInc. Keep wear above the fixed base, below the
        // independently rotating head (+1) and wreck debris (+0.25).
        if (__0 is Building_ModularTurret)
            __result = __0.def.Altitude + Altitudes.AltInc * .125f;
    }
}
