using System;
using System.Collections.Generic;
using System.Globalization;
using System.Xml;
using HarmonyLib;
using UnityEngine;
using Verse;
using ShaderRegistry = Waylith.Rendering.ShaderRegistry;

namespace ModularTurrets;

[StaticConstructorOnStartup]
internal static class ParagonItemIconPatch
{
    static ParagonItemIconPatch()
    {
        try
        {
            var original = AccessTools.DeclaredMethod(typeof(Widgets), nameof(Widgets.GetIconFor), new[]
            {
                typeof(Thing), typeof(Vector2), typeof(Rot4?), typeof(bool), typeof(float).MakeByRefType(),
                typeof(float).MakeByRefType(), typeof(Vector2).MakeByRefType(), typeof(Color).MakeByRefType(), typeof(Material).MakeByRefType()
            });
            if (original == null) throw new MissingMethodException("Widgets.GetIconFor(Thing,...)");
            new Harmony("local.modularturrets.paragon-item-icon").Patch(original,
                postfix: new HarmonyMethod(typeof(ParagonItemIconPatch), nameof(Postfix)) { priority = Priority.Last });
        }
        catch (Exception error) { ModDiagnostics.WarningOnce("Rendering.ParagonItemIconPatch", error); }
    }

    private static void Postfix(Thing thing, Material material, ref Texture __result)
    {
        if (__result == null || thing?.def == null || material != null || __result != thing.def.uiIcon
            || !ParagonVisuals.IsParagon(thing) || !CenteredPartIcons.OwnsIcon(__result)
            || thing.UIIconOverride != null || thing.StyleDef?.UIIcon != null) return;
        Texture2D replacement = ParagonVisuals.IconFor(thing.def);
        if (replacement != null) __result = replacement;
    }
}
