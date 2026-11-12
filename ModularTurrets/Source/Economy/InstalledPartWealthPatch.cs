using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using ModularTurrets.Batteries;
using RimWorld;
using Verse;

namespace ModularTurrets;

[StaticConstructorOnStartup]
internal static class InstalledPartWealthPatch
{
    static InstalledPartWealthPatch()
    {
        try
        {
            var target = AccessTools.DeclaredMethod(typeof(WealthWatcher), "CalculateWealthItems", Type.EmptyTypes);
            if (target?.ReturnType != typeof(float) || InstalledPartWealthTranspiler.Items?.FieldType != typeof(List<Thing>))
                throw new MissingMethodException("WealthWatcher item collection signature changed.");
            new Harmony("local.modularturrets.installed-part-wealth").Patch(target,
                transpiler: new HarmonyMethod(typeof(InstalledPartWealthTranspiler), nameof(InstalledPartWealthTranspiler.FilterCollectedItems)));
        }
        catch (Exception error)
        {
            InstalledPartWealth.BlockTransfer();
            Log.Warning(L.T("MT_WealthTransferUnavailable", error.Message));
        }
    }
}
