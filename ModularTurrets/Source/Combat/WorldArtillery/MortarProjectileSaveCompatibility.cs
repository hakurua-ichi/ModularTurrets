using System;
using System.Collections.Generic;
using Verse;

namespace ModularTurrets;

internal static class MortarProjectileSaveCompatibility
{
    private const string Prefix = "MT_MortarShell_";

    internal static void NormalizeDef(Thing projectile)
    {
        if (Scribe.mode != LoadSaveMode.LoadingVars || projectile?.GetType() != typeof(Projectile_ModularExplosive)
            || !ReferenceEquals(Scribe.loader.curParent, projectile)) return;
        // Stable rapid Defs, including HE, must exist before Thing reads its Def.
        // Register them even when loading a former Core shot with CE now active.
        _ = MortarShellProjectiles.Shells;
        var node = Scribe.loader.curXmlParent?["def"];
        string name = node?.InnerText;
        if (name == null || !name.StartsWith(Prefix, StringComparison.Ordinal)
            || DefDatabase<ThingDef>.GetNamedSilentFail(name) != null) return;

        var seen = new HashSet<ThingDef>();
        ThingDef candidate = null;
        foreach (var shell in DefDatabase<ThingDef>.AllDefsListForReading)
        {
            var current = MortarShellProjectiles.ProjectileFor(shell);
            if (current == null || !seen.Add(current) || current.thingClass != typeof(Projectile_ModularExplosive)
                || !current.defName.StartsWith(Prefix, StringComparison.Ordinal)
                || !ReferenceEquals(DefDatabase<ThingDef>.GetNamedSilentFail(current.defName), current)
                || !name.StartsWith(current.defName, StringComparison.Ordinal)
                || !IsDuplicateSuffix(name.AsSpan(current.defName.Length))) continue;
            // Original names ending in digits cannot be distinguished safely
            // from Core's appended random number. Do not guess by prefix length.
            if (char.IsAsciiDigit(current.defName[^1]) || candidate != null) return;
            candidate = current;
        }
        // Only this object's in-memory node changes. Its normal Scribe path
        // resolves the real Def and writes that canonical name on the next save.
        if (candidate != null) node.InnerText = candidate.defName;
    }

    private static bool IsDuplicateSuffix(ReadOnlySpan<char> suffix)
    {
        // Core DefDatabase.Add appends RoundToInt(Rand.Value * 1000): one
        // decimal number from 0 through 1000, with no leading zeroes.
        if (suffix.Length is < 1 or > 4 || suffix.Length > 1 && suffix[0] == '0') return false;
        int value = 0;
        foreach (char c in suffix)
        {
            if (!char.IsAsciiDigit(c)) return false;
            value = value * 10 + c - '0';
        }
        return value <= 1000;
    }
}
