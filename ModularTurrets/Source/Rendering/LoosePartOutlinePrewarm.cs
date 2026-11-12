using System;
using UnityEngine;
using Verse;

namespace ModularTurrets;

[StaticConstructorOnStartup]
internal static class LoosePartOutlinePrewarm
{
    static LoosePartOutlinePrewarm()
    {
        ModContentPack content = ModularTurretsMod.Instance?.Content;
        if (content == null) return;
        // PlayDataLoader invokes all startup constructors before its normal
        // static atlas bake. No texture generation, extra bake or map polling.
        foreach (ThingDef definition in DefDatabase<ThingDef>.AllDefsListForReading)
        {
            if (definition.modContentPack != content || definition.category != ThingCategory.Item
                || definition.graphicData?.graphicClass != typeof(Graphic_LoosePartOutline)) continue;
            try
            {
                definition.graphicData.Graphic?.TryInsertIntoAtlas(TextureAtlasGroup.Item);
            }
            catch (Exception error)
            {
                Log.Warning(L.T("MT_ItemOutlineInitializationFailed", definition.defName, error.Message));
            }
        }
    }
}
