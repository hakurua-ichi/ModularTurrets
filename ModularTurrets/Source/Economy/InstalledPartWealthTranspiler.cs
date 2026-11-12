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

internal static class InstalledPartWealthTranspiler
{
    internal static readonly FieldInfo Items = AccessTools.DeclaredField(typeof(WealthWatcher), "tmpThings");

    internal static IEnumerable<CodeInstruction> FilterCollectedItems(IEnumerable<CodeInstruction> instructions)
    {
        var code = instructions.ToList();
        var calls = code.Select((instruction, index) => (instruction, index)).Where(x =>
            x.instruction.operand is MethodInfo method && method.DeclaringType == typeof(ThingOwnerUtility)
            && method.Name == nameof(ThingOwnerUtility.GetAllThingsRecursively) && method.IsGenericMethod
            && method.GetGenericArguments().SequenceEqual(new[] { typeof(Thing) })
            && method.GetParameters().Select(p => p.ParameterType).SequenceEqual(new[]
                { typeof(Map), typeof(ThingRequest), typeof(List<Thing>), typeof(bool), typeof(Predicate<IThingHolder>), typeof(bool) })).ToArray();
        if (calls.Length != 1 || Items == null || !code.Take(calls[0].index).Any(i => i.LoadsField(Items)))
        {
            InstalledPartWealth.BlockTransfer();
            Log.Warning(L.T("MT_WealthCoreShapeChanged"));
            return code;
        }
        code.InsertRange(calls[0].index + 1, new[]
        {
            new CodeInstruction(OpCodes.Ldarg_0), new CodeInstruction(OpCodes.Ldfld, Items),
            new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(InstalledPartWealth), nameof(InstalledPartWealth.RemoveEmbeddedParts)))
        });
        InstalledPartWealth.MarkReady();
        return code;
    }
}
