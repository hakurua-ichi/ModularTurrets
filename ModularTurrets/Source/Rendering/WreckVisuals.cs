using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace ModularTurrets;

public static class WreckVisuals
{
    private static readonly Dictionary<Graphic, Graphic> cache = new();
    private static Dictionary<string, WreckTextureReplacement> replacements;
    private static List<WreckDebris> debris;

    private static void Prepare()
    {
        if (replacements != null) return;
        replacements = new(); debris = new();
        foreach (var set in DefDatabase<WreckVisualSetDef>.AllDefsListForReading)
        {
            foreach (var item in set.replacements) replacements[item.normal] = item;
            debris.AddRange(set.debris);
        }
    }

    public static Graphic Resolve(Graphic original, out Vector3 offset)
    {
        Prepare(); offset = Vector3.zero;
        if (original == null) return null;
        replacements.TryGetValue(original.path ?? string.Empty, out var replacement);
        if (replacement != null) offset = replacement.offset;
        if (cache.TryGetValue(original, out var result)) return result;
        if (replacement != null)
        {
            result = GraphicDatabase.Get<Graphic_Single>(replacement.damaged, original.Shader,
                original.drawSize, original.color, original.colorTwo, original.data);
            // GraphicData participates in the cache key, preserving each building offset.
        }
        else
        {
            // Future or external parts without a damaged texture retain a dark silhouette.
            result = original.GetColoredVersion(original.Shader,
                original.color * new Color(.58f, .58f, .58f, 1f),
                original.colorTwo * new Color(.58f, .58f, .58f, 1f));
        }
        cache[original] = result;
        return result;
    }

    public static void DrawDebris(Building_ModularTurret turret, Vector3 location)
    {
        Prepare();
        foreach (var item in debris)
        {
            if (turret.PartAt(item.slot)?.def != item.module) continue;
            item.material ??= MaterialPool.MatFrom(item.texture, ShaderDatabase.Cutout);
            Vector3 point = location + item.offset + Altitudes.AltIncVect * .25f;
            Graphics.DrawMesh(MeshPool.plane10, Matrix4x4.TRS(point, Quaternion.identity,
                new Vector3(item.size.x, 1f, item.size.y)), item.material, 0);
        }
    }
}
