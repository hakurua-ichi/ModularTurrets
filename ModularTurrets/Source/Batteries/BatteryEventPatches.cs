using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using Verse;

namespace ModularTurrets.Batteries;

[StaticConstructorOnStartup]
internal static class BatteryEventPatches
{
    static BatteryEventPatches()
    {
        var harmony = new Harmony("local.modularturrets.battery-modules");
        Patch(typeof(CompBreakdownable), nameof(CompBreakdownable.DoBreakdown), nameof(AllowBreakdown));
        Patch(typeof(PowerNetGraphics), nameof(PowerNetGraphics.PrintWirePieceConnecting), nameof(ShowWire));
        Patch(typeof(ShortCircuitUtility), nameof(ShortCircuitUtility.TryShortCircuitInRain), nameof(AllowRainShort));
        Patch(typeof(ShortCircuitUtility), nameof(ShortCircuitUtility.DoShortCircuit), nameof(IncidentBatteries), true);
        Patch(typeof(ShortCircuitUtility), "DrainBatteriesAndCauseExplosion", nameof(IncidentBatteries), true);

        void Patch(Type type, string method, string hook, bool transpiler = false)
        {
            try
            {
                var target = AccessTools.DeclaredMethod(type, method) ?? throw new MissingMethodException(type.FullName, method);
                var patch = new HarmonyMethod(typeof(BatteryEventPatches), hook);
                if (transpiler) harmony.Patch(target, transpiler: patch);
                else harmony.Patch(target, prefix: patch);
            }
            catch (Exception ex) { ModDiagnostics.WarningOnce("Batteries.EventPatch/" + method, ex); }
        }
    }

    private static bool AllowBreakdown(CompBreakdownable __instance)
        => __instance.parent is not Building_ModularBattery battery || battery.Battery?.PreventsBreakdown != true;

    // Core's natural rain candidates already respect shortCircuitInRain. Direct callers
    // otherwise bypass that filter for any battery containing more than 100 Wd.
    private static bool AllowRainShort(Thing thing)
        => thing is not Building_ModularBattery battery || battery.Battery?.Props.shortCircuitInRain != false;

    // Physical wires and purple overlay lines share this mesh print. Connector
    // bases and conduit graphics are printed separately and stay visible.
    private static bool ShowWire(Thing A, Thing B, bool forPowerOverlay)
    {
        if (B is not Building_ModularBattery battery || battery.Battery is not { WirelessReceivers.Count: > 0 } source) return true;
        var receiver = (A as Building)?.PowerComp;
        bool wireless = receiver?.connectParent == source && source.WirelessReceivers.Contains(A);
        if (!forPowerOverlay || A.def.building?.supportsWallAttachments != true || source.connectChildren == null)
            return !wireless;
        // Core uses the wall as the overlay endpoint for a mounted device. A single
        // wall can also carry a normal wired device connected to the same battery.
        if (receiver?.connectParent == source && !wireless) return true;
        foreach (var child in source.connectChildren)
        {
            var attachment = child.parent;
            if (child.connectParent != source || !attachment.Spawned || attachment.def.building?.isAttachment != true
                || GenConstruct.GetWallAttachedTo(attachment) != A) continue;
            if (!source.WirelessReceivers.Contains(attachment)) return true;
            wireless = true;
        }
        return !wireless;
    }

    // Each incident method gets its own list. Other incident effects and the live
    // PowerNet collection remain available to Core and other mods throughout explosions.
    private static List<CompPowerBattery> EligibleBatteries(PowerNet net, ref List<CompPowerBattery> local)
    {
        if (local != null) return local;
        var all = net.batteryComps;
        foreach (var battery in all)
            if (battery is CompModularBattery { ProtectsFromShortCircuit: true })
                return local = all.FindAll(static b => b is not CompModularBattery { ProtectsFromShortCircuit: true });
        return local = all;
    }

    private static IEnumerable<CodeInstruction> IncidentBatteries(IEnumerable<CodeInstruction> instructions,
        ILGenerator generator, MethodBase __originalMethod)
    {
        var code = instructions.ToList();
        var field = AccessTools.Field(typeof(PowerNet), nameof(PowerNet.batteryComps));
        int expected = __originalMethod.Name == nameof(ShortCircuitUtility.DoShortCircuit) ? 1 : 2;
        if (code.Count(i => i.LoadsField(field)) != expected)
        {
            Log.Warning($"[ModularTurrets] Short circuit protection skipped for changed {__originalMethod.Name}.");
            return code;
        }
        var cache = generator.DeclareLocal(typeof(List<CompPowerBattery>));
        var filter = AccessTools.Method(typeof(BatteryEventPatches), nameof(EligibleBatteries));
        for (int i = code.Count - 1; i >= 0; i--)
        {
            if (!code[i].LoadsField(field)) continue;
            // The PowerNet reference already occupies the stack. Keep the original
            // instruction's labels/exception blocks on the replacement local-address load.
            code[i].opcode = OpCodes.Ldloca;
            code[i].operand = cache;
            code.Insert(i + 1, new CodeInstruction(OpCodes.Call, filter));
        }
        code.InsertRange(0, new[] { new CodeInstruction(OpCodes.Ldnull), new CodeInstruction(OpCodes.Stloc, cache) });
        return code;
    }
}
