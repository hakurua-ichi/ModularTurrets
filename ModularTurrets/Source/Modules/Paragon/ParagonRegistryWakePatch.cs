using System;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Verse;

namespace ModularTurrets;

// A direct inventory/container import need not spawn anything on the map.
[StaticConstructorOnStartup]
internal static class ParagonRegistryWakePatch
{
    internal static bool Available { get; private set; }

    static ParagonRegistryWakePatch() => Initialize();

    internal static void Initialize()
    {
        if (Available) return;
        try { ApplyPatch(); Available = true; }
        catch (Exception error)
        {
            Log.ErrorOnce("[Modular Turrets] Inventory wake hook unavailable; retaining periodic paragon scans. "
                + error, 1724213711);
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ApplyPatch()
    {
        var original = AccessTools.DeclaredMethod(typeof(ThingOwner), "NotifyAdded", new[] { typeof(Thing) });
        if (original == null) throw new MissingMethodException(typeof(ThingOwner).FullName, "NotifyAdded(Thing)");
        new Harmony("local.modularturrets.paragon-inventory-wake").Patch(original,
            postfix: new HarmonyMethod(typeof(ParagonRegistryWakePatch), nameof(AfterAdded)));
    }

    private static void AfterAdded(Thing item) => ParagonMapRegistry.NotifyHeldArrival(item);
}
