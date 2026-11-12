using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using RimWorld;
using UnityEngine;
using Verse;

namespace ModularTurrets;

public sealed partial class Building_ModularTurret
{
    private readonly struct ShotMuzzle
    {
        public readonly Thing Equipment;
        public readonly string Slot;
        public readonly Vector3 Point;
        public readonly int Index, Count;
        public readonly FleckDef Flash;
        public ShotMuzzle(Thing equipment, string slot, Vector3 point, int index, int count, FleckDef flash = null)
        { Equipment = equipment; Slot = slot; Point = point; Index = index; Count = count; Flash = flash; }
    }
    private ShotMuzzle? currentShotMuzzle;
    public Vector3 CurrentShotMuzzlePosition(Thing equipment)
        => currentShotMuzzle is { } active && ReferenceEquals(active.Equipment, equipment)
            ? active.Point : ResolveShotMuzzle(equipment).Point;

    // Pure reads use the next muzzle, never advance it. Every consumer inside
    // this scope gets one fixed world point even when several projectiles spawn.
    public bool WithShotMuzzle(Verb verb, Thing equipment, Func<bool> action)
    {
        float flashScale = ShotMuzzleFlash.SuppressVanilla(verb);
        if (currentShotMuzzle is { } active && ReferenceEquals(active.Equipment, equipment)) return action();
        ShotMuzzle shot = ResolveShotMuzzle(equipment);
        if (Spawned && !shot.Point.InBounds(Map)) return false;
        ShotMuzzle? previous = currentShotMuzzle;
        currentShotMuzzle = shot;
        try
        {
            bool fired = action();
            if (fired)
            {
                (muzzleIndices ??= new Dictionary<string, int>())[shot.Slot] = (shot.Index + 1) % shot.Count;
                ShotMuzzleFlash.Emit(this, shot.Point, flashScale, shot.Flash);
            }
            return fired;
        }
        finally { currentShotMuzzle = previous; }
    }

    private ShotMuzzle ResolveShotMuzzle(Thing equipment)
    {
        FireChannel channel = channels.FirstOrDefault(c => c.Gun == equipment);
        string slot = channel?.Slot ?? "head";
        Thing part = channel?.Part ?? (slot == "head" ? HeadPart : PartAt(slot));
        PartExtension definition = part?.def.GetModExtension<PartExtension>();
        PartAnchor anchor = definition?.anchors?.FirstOrDefault(a => a.slot == slot);
        int next = muzzleIndices != null && muzzleIndices.TryGetValue(slot, out int value) ? Math.Max(0, value) : 0;
        Vector3 location = DrawPos + Altitudes.AltIncVect;
        float size = definition?.headSize ?? 1f;
        if (slot != "head")
        {
            MountPoint mount = Head?.mounts?.FirstOrDefault(m => m.id == slot);
            ModuleMountOverride placement = mount?.moduleOverrides?.FirstOrDefault(m => m.moduleKind == definition?.moduleKind);
            location += (placement?.offset ?? mount?.offset ?? Vector3.zero).RotatedBy(aimAngle);
            location.y = DrawPos.y + Altitudes.AltInc * (slot == "top" ? 3f : 2f);
            size = placement?.size ?? mount?.size ?? .65f;
        }
        var points = anchor?.muzzles;
        GraphicData graphic = anchor?.graphicData ?? part?.def.graphicData;
        if (points != null && points.Count > 0 && graphic != null)
        {
            int index = next % points.Count;
            Vector2 pivot = anchor.pivot;
            float angle = aimAngle + anchor.angle;
            if (float.IsFinite(anchor.rotationDegreesPerSecond) && anchor.rotationDegreesPerSecond != 0f)
            {
                double phase = ((double)(Find.TickManager?.TicksGame ?? 0) * ((double)anchor.rotationDegreesPerSecond / 60d % 360d)) % 360d;
                angle += (float)(phase < 0d ? phase + 360d : phase);
            }
            if (anchor.fixedGraphicData != null) location += Altitudes.AltIncVect * .5f;
            Vector3 point = MuzzleGeometry.ImagePoint(location, pivot, graphic.drawSize * size, angle, anchor.flipX, points[index]);
            return new ShotMuzzle(equipment, slot, point, index, points.Count, anchor.muzzleFlash);
        }
        // Existing saves/third-party parts without new metadata keep their old
        // approximate origin, but still benefit from a stable per-shot snapshot.
        bool dual = slot == "head" && part?.def.defName.Contains("Dual") == true;
        int count = dual ? 2 : 1, fallbackIndex = next % count;
        float lateral = dual ? (fallbackIndex == 0 ? -.28f : .28f) : 0f;
        Vector3 fallback = location + new Vector3(lateral, 0f, slot == "head" ? .65f : .4f).RotatedBy(aimAngle);
        return new ShotMuzzle(equipment, slot, fallback, fallbackIndex, count);
    }
}
