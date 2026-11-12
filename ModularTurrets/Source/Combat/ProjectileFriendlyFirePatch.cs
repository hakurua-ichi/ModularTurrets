using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using RimWorld;
using Verse;

namespace ModularTurrets;

[StaticConstructorOnStartup]
internal static class ProjectileFriendlyFirePatch
{
    private const string OwnerId = "local.modularturrets.orphaned-projectile-iff";
    private static bool initialized, warnedInterceptShape;

    static ProjectileFriendlyFirePatch() => Initialize();

    internal static void Initialize()
    {
        if (initialized) return;
        try
        {
            ApplyPatch();
            initialized = true;
        }
        catch (Exception exception)
        {
            ModDiagnostics.ErrorOnce("Combat.FriendlyFirePatch", exception);
        }
    }

    // Keep Harmony type loading inside the guarded call, including a missing dependency.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ApplyPatch()
    {
        var original = AccessTools.Method(typeof(Projectile), "CanHit", new[] { typeof(Thing) });
        if (original == null || original.ReturnType != typeof(bool))
            throw new MissingMethodException(typeof(Projectile).FullName, "CanHit(Thing)");
        var intercept = AccessTools.Method(typeof(Projectile), "CheckForFreeIntercept", new[] { typeof(IntVec3) });
        if (intercept == null || intercept.ReturnType != typeof(bool))
            throw new MissingMethodException(typeof(Projectile).FullName, "CheckForFreeIntercept(IntVec3)");
        var harmony = new Harmony(OwnerId);
        harmony.Patch(original,
            postfix: new HarmonyMethod(typeof(ProjectileFriendlyFirePatch), nameof(CanHitPostfix)));
        harmony.Patch(intercept,
            transpiler: new HarmonyMethod(typeof(ProjectileFriendlyFirePatch), nameof(AggressivePawnInterception)));
    }

    private static void CanHitPostfix(Projectile __instance, Thing __0, ref bool __result)
    {
        if (__instance is Projectile_ModularBullet bullet && bullet.HasAlreadyPenetrated(__0)) { __result = false; return; }
        if (__result && __instance is Projectile_ModularExplosive explosive && explosive.ProtectsMortarBuildings
            && FriendlyFireSafety.IsProtectedBuilding(__0, explosive.SavedProtectionFaction ?? __instance.Launcher?.Faction))
        { __result = false; return; }
        if (!__result || __instance.Launcher != null || __0 is not Pawn) return;

        // Core's friendly interception rule needs a launcher. Once that reference is
        // gone, only our exact projectile types use their saved launch-time protection.
        // Do not change third-party subclasses or restore a rejected hit candidate.
        if (FriendlyFireSafety.IsProtectedPawn(__0, ProtectionFaction(__instance))) __result = false;
    }

    private static Faction ProtectionFaction(Projectile projectile)
    {
        Type type = projectile.GetType();
        if (type == typeof(Projectile_ModularBullet))
            return ((Projectile_ModularBullet)projectile).SavedProtectionFaction;
        else if (type == typeof(Projectile_ModularExplosive) || type == typeof(Projectile_ModularMissile))
            return ((Projectile_ModularExplosive)projectile).SavedProtectionFaction;
        return null;
    }

    private static IEnumerable<CodeInstruction> AggressivePawnInterception(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
    {
        var code = instructions.ToList();
        int callIndex, pawnIndex;
        MethodInfo filter;
        try
        {
            if (!TryFindInterception(code, __originalMethod, out callIndex, out pawnIndex))
            {
                WarnInterceptShape("Projectile.CheckForFreeIntercept Pawn/faction branch contract changed.");
                return code;
            }
            filter = AccessTools.Method(typeof(ProjectileFriendlyFirePatch), nameof(HostileForInterception));
            if (filter == null) throw new MissingMethodException(nameof(HostileForInterception));
        }
        catch (Exception exception) { ModDiagnostics.WarningOnce("Combat.InterceptTranspiler", exception); WarnInterceptShape(exception.Message); return code; }

        // Other mods can rerun this transpiler after Initialize has returned.
        // Retain every existing instruction, label and exception block; add only
        // the same bool filter after a positively identified faction check.
        code.InsertRange(callIndex + 1, new[] { new CodeInstruction(OpCodes.Ldarg_0),
            CodeInstruction.LoadLocal(pawnIndex), new CodeInstruction(OpCodes.Call, filter) });
        return code;
    }

    private static bool TryFindInterception(List<CodeInstruction> code, MethodBase original, out int callIndex, out int pawnIndex)
    {
        callIndex = pawnIndex = -1;
        var locals = original.GetMethodBody()?.LocalVariables;
        var pawns = locals?.Where(v => v.LocalType == typeof(Pawn)).ToArray();
        if (pawns?.Length != 1) return false;
        pawnIndex = pawns[0].LocalIndex;
        int local = pawnIndex;
        var hostility = AccessTools.Method(typeof(FactionUtility), nameof(FactionUtility.HostileTo), new[] { typeof(Faction), typeof(Faction) });
        var faction = AccessTools.PropertyGetter(typeof(Thing), nameof(Thing.Faction));
        var launcher = AccessTools.Field(typeof(Projectile), "launcher");
        if (hostility == null || faction == null || launcher == null
            || code.Count(i => i.opcode == OpCodes.Call && Equals(i.operand, hostility)) != 1) return false;
        callIndex = code.FindIndex(i => i.opcode == OpCodes.Call && Equals(i.operand, hostility));
        if (callIndex < 5 || callIndex + 1 >= code.Count
            || !LoadsLocal(code[callIndex - 5], local) || !code[callIndex - 4].Calls(faction)
            || !code[callIndex - 3].IsLdarg(0) || !code[callIndex - 2].LoadsField(launcher)
            || !code[callIndex - 1].Calls(faction)
            || (code[callIndex + 1].opcode != OpCodes.Brtrue && code[callIndex + 1].opcode != OpCodes.Brtrue_S)
            || code[callIndex + 1].operand is not Label exit
            || code[callIndex + 1].labels.Count != 0
            || !code.Skip(callIndex + 2).Any(i => i.labels.Contains(exit))
            || code.Skip(callIndex - 5).Take(7).Any(i => i.blocks.Count != 0)) return false;

        int cast = code.FindIndex(i => i.opcode == OpCodes.Isinst && Equals(i.operand, typeof(Pawn)));
        return cast >= 1 && cast + 3 < callIndex - 5
            && code[cast - 1].IsLdloc() && LoadsLocal(code[cast - 1], code[cast - 1].LocalIndex())
            && locals[code[cast - 1].LocalIndex()].LocalType == typeof(Thing)
            && code[cast + 1].IsStloc() && code[cast + 1].LocalIndex() == local
            && code.Count(i => i.IsStloc() && i.LocalIndex() == local) == 1
            && LoadsLocal(code[cast + 2], local)
            && (code[cast + 3].opcode == OpCodes.Brfalse || code[cast + 3].opcode == OpCodes.Brfalse_S)
            && code[cast + 3].operand is Label notPawn
            && code.Skip(callIndex + 2).Any(i => i.labels.Contains(notPawn));
    }

    private static bool LoadsLocal(CodeInstruction instruction, int index)
        => instruction.IsLdloc() && instruction.opcode != OpCodes.Ldloca && instruction.opcode != OpCodes.Ldloca_S
            && instruction.LocalIndex() == index;

    private static void WarnInterceptShape(string reason)
    {
        if (warnedInterceptShape) return;
        warnedInterceptShape = true;
        try { Log.Warning(L.T("MT_IffInterceptShapeSkipped", reason)); }
        catch { /* A diagnostic must not reject another mod's patch application. */ }
    }

    // Core repeats a faction-only friendly check after CanHit. Exclude aggressive
    // mental states there too, without changing any projectile or faction state.
    private static bool HostileForInterception(bool hostile, Projectile projectile, Pawn pawn)
        => hostile || ProtectionFaction(projectile) != null && pawn.InAggroMentalState;
}
