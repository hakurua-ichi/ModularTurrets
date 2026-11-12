using System;
using System.Collections.Generic;
using Verse;

namespace ModularTurrets;

// Optional assemblies own their combat APIs and saved state. A registered
// remote backend is authoritative; failed orders never retry Core transport.
[StaticConstructorOnStartup]
public static class CombatCompatibility
{
    private static ICombatCompatibility provider;
    private static IRemoteMortarBackend remote;
    private static readonly bool required = ModLister.AnyModActiveNoSuffix(new[] { "ceteam.combatextended" });
    private static bool reportedUnavailable;
    public static ICombatCompatibility Provider => provider;
    public static IRemoteMortarBackend Remote => remote;
    public static bool IsAvailable => !required || provider != null && remote != null;
    internal static bool CheckReady()
    {
        if (IsAvailable) return true;
        if (!reportedUnavailable)
        {
            reportedUnavailable = true;
            Log.Error("[Modular Turrets] Combat Extended is active but its optional turret adapter did not initialize. Turret firing and world artillery are stopped; check the compatibility assembly and CE version.");
        }
        return false;
    }
    public static void Register(ICombatCompatibility combat, IRemoteMortarBackend remote)
    {
        if (combat == null) throw new ArgumentNullException(nameof(combat));
        if (remote == null) throw new ArgumentNullException(nameof(remote));
        if (provider != null && (!ReferenceEquals(provider, combat) || !ReferenceEquals(CombatCompatibility.remote, remote)))
            throw new InvalidOperationException("A modular turret combat adapter is already registered.");
        provider = combat;
        CombatCompatibility.remote = remote;
    }
}
