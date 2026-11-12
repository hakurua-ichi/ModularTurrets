using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using HarmonyLib;
using Verse;

namespace ModularTurrets.Compat.VisibleWealth;

internal static class VisibleWealthTranspiler
{

    internal static IEnumerable<CodeInstruction> FilterItemNode(IEnumerable<CodeInstruction> instructions)
    {
        var code = instructions.ToList();
        var removeAll = AccessTools.Method(typeof(List<Thing>), nameof(List<Thing>.RemoveAll), new[] { typeof(Predicate<Thing>) });
        var addRange = AccessTools.Method(typeof(List<Thing>), nameof(List<Thing>.AddRange), new[] { typeof(IEnumerable<Thing>) });
        var calls = code.Select((instruction, index) => (instruction, index)).Where(x => x.instruction.Calls(removeAll)).ToArray();
        // VisibleWealth 1.1.3 first filters minified definitions, joins those
        // items, then filters the final list before both quantity and value.
        var joins = code.Select((instruction, index) => (instruction, index)).Where(x => x.instruction.Calls(addRange)).ToArray();
        if (calls.Length != 2 || joins.Length != 1 || calls[0].index >= joins[0].index || joins[0].index >= calls[1].index)
        {
            InstalledPartWealth.BlockTransfer();
            Log.Warning(L.T("MT_WealthBreakdownShapeChanged"));
            return code;
        }
        var replacement = new CodeInstruction(code[calls[1].index])
        {
            opcode = OpCodes.Call,
            operand = AccessTools.Method(typeof(InstalledPartWealth), nameof(InstalledPartWealth.RemoveExcludedItems))
        };
        code[calls[1].index] = replacement;
        return code;
    }
}
