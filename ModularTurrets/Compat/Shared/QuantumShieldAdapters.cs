using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace ModularTurrets.Compat.QuantumShields;

// Startup registration, shared as private code by independent optional DLLs.
// No assembly/type discovery or reflection is performed in the damage hooks.
internal static class QuantumShieldAdapters
{
    private static readonly HashSet<string> packages = new(StringComparer.OrdinalIgnoreCase);

    internal static void RegisterComp(string owner, string package, string typeName)
        => Register(owner, package, typeName, "PostPreApplyDamage", typeof(ThingComp), typeof(void),
            new[] { typeof(DamageInfo).MakeByRefType(), typeof(bool).MakeByRefType() }, nameof(BeforeComp));

    internal static void RegisterApparel(string owner, string package, string typeName)
        => Register(owner, package, typeName, "CheckPreAbsorbDamage", typeof(Apparel), typeof(bool),
            new[] { typeof(DamageInfo) }, nameof(BeforeApparel));

    private static void Register(string owner, string package, string typeName, string methodName,
        Type baseType, Type result, Type[] arguments, string prefix)
    {
        try
        {
            var type = AccessTools.TypeByName(typeName);
            var method = type == null ? null : AccessTools.DeclaredMethod(type, methodName, arguments);
            if (method?.ReturnType != result || !baseType.IsAssignableFrom(type))
                throw new MissingMethodException(typeName, methodName);
            packages.Add(package);
            new Harmony(owner).Patch(method, prefix: new HarmonyMethod(typeof(QuantumShieldAdapters), prefix) { priority = Priority.Last });
        }
        catch (Exception error) { Warn(package, error); }
    }

    internal static void Warn(string package, Exception error)
        => Log.Warning(L.T("MT_QuantumShieldAdapterFailed", package, error.Message));

    private static bool Owns(Def def) => def?.modContentPack?.PackageIdPlayerFacing is { } package && packages.Contains(package);

    internal static bool BeforeComp(ThingComp __instance, DamageInfo __0, ref bool __1, bool __runOriginal)
    {
        if (!__runOriginal || !QuantumDamage.Matches(__0) || !Owns(__instance?.parent?.def)) return true;
        __1 = false;
        return false;
    }

    internal static bool BeforeApparel(Apparel __instance, DamageInfo __0, ref bool __result, bool __runOriginal)
    {
        if (!__runOriginal || !QuantumDamage.Matches(__0) || !Owns(__instance?.def)) return true;
        __result = false;
        return false;
    }
}
