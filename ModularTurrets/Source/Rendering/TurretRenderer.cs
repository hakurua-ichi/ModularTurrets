using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using Verse;

namespace ModularTurrets;

[StaticConstructorOnStartup]
public static class TurretRenderer
{
    private static Material shieldMaterial;

    public static void Draw(Building_ModularTurret turret, Vector3 drawLoc)
    {
        Thing head = turret.HeadPart;
        PartExtension definition = head?.def.GetModExtension<PartExtension>();
        if (definition == null) return;
        float angle = turret.AimAngle;
        Vector3 center = drawLoc + Altitudes.AltIncVect;
        if (turret.IsWrecked) WreckVisuals.DrawDebris(turret, drawLoc);
        DrawPart(head, center, definition.headSize, angle, "head", turret.IsWrecked);
        if (definition.mounts != null)
        {
            foreach (MountPoint mount in definition.mounts)
            {
                Thing module = turret.PartAt(mount.id);
                if (module == null) continue;
                string kind = module.def.GetModExtension<PartExtension>()?.moduleKind;
                ModuleMountOverride placement = null;
                foreach (var candidate in CollectionsMarshal.AsSpan(mount.moduleOverrides))
                    if (candidate.moduleKind == kind) { placement = candidate; break; }
                Vector3 location = center + (placement?.offset ?? mount.offset).RotatedBy(angle);
                location.y = center.y + Altitudes.AltInc * (mount.id == "top" ? 2f : 1f);
                DrawPart(module, location, placement?.size ?? mount.size, angle, mount.id, turret.IsWrecked);
            }
        }
        if (turret.ShieldVisible)
        {
            shieldMaterial ??= MaterialPool.MatFrom("Other/ShieldBubble", ShaderDatabase.Transparent,
                new Color(0.3f, 0.85f, 1f, 0.16f));
            Vector3 shieldLoc = center + Altitudes.AltIncVect * 3f;
            Graphics.DrawMesh(MeshPool.plane10,
                Matrix4x4.TRS(shieldLoc, Quaternion.identity, new Vector3(2.9f, 1f, 2.9f)), shieldMaterial, 0);
        }
    }

    private static void DrawPart(Thing part, Vector3 location, float size, float angle, string slot, bool wrecked)
    {
        PartExtension definition = part.def.GetModExtension<PartExtension>();
        int index = definition?.atlasIndex ?? 0;
        Vector2 pivot = definition?.graphicPivot ?? new Vector2(-1f, -1f);
        if (pivot.x < 0f || pivot.y < 0f) pivot = new Vector2(0.5f, PartAtlas.PivotY(index));
        PartAnchor anchor = null;
        foreach (var candidate in CollectionsMarshal.AsSpan(definition?.anchors))
            if (candidate.slot == slot) { anchor = candidate; break; }
        if (anchor != null) { pivot = anchor.pivot; angle += anchor.angle; }
        bool flipX = anchor?.flipX ?? false;
        // Side canvases overlap at the same depth, so retain their original order
        // without paying for single-instance batches on every turret.
        bool batch = !wrecked && (slot is "head" or "top");
        bool paragon = !wrecked && definition?.isHead == false && ParagonVisuals.IsParagon(part);
        Graphic graphic = anchor?.graphicData?.Graphic ?? part.Graphic;
        if (anchor?.fixedGraphicData != null)
        {
            Graphic fixedGraphic = anchor.fixedGraphicData.Graphic;
            Vector3 fixedOffset = Vector3.zero;
            if (wrecked) fixedGraphic = WreckVisuals.Resolve(fixedGraphic, out fixedOffset);
            DrawGraphic(fixedGraphic, location + fixedOffset.RotatedBy(angle), size, angle, anchor.fixedPivot, flipX, batch);
            location += Altitudes.AltIncVect * 0.5f;
        }
        if (!wrecked) angle += RotationPhase(anchor?.rotationDegreesPerSecond ?? 0f);
        if (wrecked)
        {
            graphic = WreckVisuals.Resolve(graphic, out Vector3 damageOffset);
            location += damageOffset.RotatedBy(angle);
        }
        DrawGraphic(graphic, location, size, angle, pivot, flipX, batch, paragon);
        if (!wrecked && anchor?.beam != null)
            ModuleBeamRenderer.Draw(anchor.beam, location, pivot, graphic.drawSize * size, angle, flipX);
    }

    private static float RotationPhase(float degreesPerSecond)
    {
        if (degreesPerSecond == 0f || !float.IsFinite(degreesPerSecond)) return 0f;
        int ticks = Find.TickManager?.TicksGame ?? 0;
        // Reduce in double precision before casting, even after years of game ticks.
        double perTick = ((double)degreesPerSecond / 60d) % 360d;
        double phase = ((double)ticks * perTick) % 360d;
        return (float)(phase < 0d ? phase + 360d : phase);
    }

    private static void DrawGraphic(Graphic graphic, Vector3 location, float size, float angle, Vector2 pivot, bool flipX, bool batch, bool paragon = false)
    {
        float width = size * graphic.drawSize.x;
        float depth = size * graphic.drawSize.y;
        // The mirrored image's physical contact moves across its center with the UVs.
        float pivotOffsetX = (0.5f - pivot.x) * width * (flipX ? -1f : 1f);
        location += new Vector3(pivotOffsetX, 0f, (pivot.y - 0.5f) * depth).RotatedBy(angle);
        Material material = graphic.MatSingle;
        if (paragon) material = ParagonVisuals.MaterialFor(material, graphic.path);
        TurretRenderBatch.DrawMesh(flipX ? MeshPool.plane10Flip : MeshPool.plane10,
            Matrix4x4.TRS(location, Quaternion.AngleAxis(angle, Vector3.up), new Vector3(width, 1f, depth)), material, 0, batch);
    }
}
