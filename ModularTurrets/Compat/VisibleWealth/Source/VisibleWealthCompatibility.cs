using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using HarmonyLib;
using Verse;

namespace ModularTurrets.Compat.VisibleWealth;

[StaticConstructorOnStartup]
internal static class VisibleWealthCompatibility
{
    static VisibleWealthCompatibility()
    {
        if (!ModLister.AnyModActiveNoSuffix(new[] { "visiblewealth.1trickPwnyta" })) return;
        try
        {
            var target = AccessTools.Constructor(typeof(global::VisibleWealth.WealthNode_Item),
                new[] { typeof(global::VisibleWealth.WealthNode), typeof(Map), typeof(int), typeof(ThingDef) });
            if (target == null) throw new MissingMethodException("VisibleWealth item node constructor changed.");
            new Harmony("local.modularturrets.visiblewealth").Patch(target,
                transpiler: new HarmonyMethod(typeof(VisibleWealthTranspiler), nameof(VisibleWealthTranspiler.FilterItemNode)));
        }
        catch (Exception error)
        {
            InstalledPartWealth.BlockTransfer();
            Log.Warning(L.T("MT_WealthBreakdownUnavailable", error.Message));
        }
    }
}
