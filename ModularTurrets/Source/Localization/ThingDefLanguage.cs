using System.Collections.Generic;
using RimWorld;
using Verse;

namespace ModularTurrets;

[StaticConstructorOnStartup]
internal static class ThingDefLanguage
{
    private static Dictionary<ThingDef, (string Label, string Description)> originalText;

    // Defs and the game's translations are ready here. Earlier translation
    // requests may have run before the language selector definitions existed.
    static ThingDefLanguage() => L.Reload();

    internal static void Apply()
    {
        var mod = ModularTurretsMod.Instance;
        if (mod?.Content == null) return;
        if (originalText == null)
        {
            var originals = new Dictionary<ThingDef, (string, string)>();
            foreach (var definition in DefDatabase<ThingDef>.AllDefsListForReading)
                if (definition.modContentPack == mod.Content)
                    originals.Add(definition, (definition.label, definition.description));
            if (originals.Count == 0) return;
            originalText = originals;
        }

        bool automatic = (mod.Settings.CurrentLanguage ?? "auto") == "auto";
        bool changed = false;
        foreach (var entry in originalText)
        {
            var definition = entry.Key;
            string label = automatic ? entry.Value.Label
                : L.DefinitionText(definition, "label", entry.Value.Label);
            string description = automatic ? entry.Value.Description
                : L.DefinitionText(definition, "description", entry.Value.Description);
            if (definition.label == label && definition.description == description) continue;
            definition.label = label;
            definition.description = description;
            definition.ClearCachedData();
            changed = true;
        }

        // Existing things, blueprints and multi-selection read these shared Defs.
        // Clear cached composed labels once per switch; no per-thing/tick hook.
        if (changed) GenLabel.ClearCache();
    }
}
