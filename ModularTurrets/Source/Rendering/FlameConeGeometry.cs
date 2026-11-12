using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace ModularTurrets;

public static class FlameConeGeometry
{
    public static float Angle(Thing equipment)
    {
        float angle = equipment?.def.GetModExtension<HeadAccuracyExtension>()?.flameConeAngle ?? 90f;
        return float.IsFinite(angle) ? Math.Clamp(angle, 1f, 180f) : 90f;
    }

    // Use the base centre and current head bearing for both combat and overlays.
    // Range retains the existing centre-based calculation, not muzzle + range.
    public static bool Contains(Vector3 origin, float facing, float angle, float range, Vector3 point)
    {
        if (!float.IsFinite(range) || range <= 0f || !float.IsFinite(facing) || !float.IsFinite(angle)) return false;
        Vector3 offset = (point - origin).Yto0();
        float distance = offset.magnitude;
        if (!float.IsFinite(distance) || distance > range + 0.0001f) return false;
        if (distance < 0.0001f) return true;
        float minimum = Mathf.Cos(Math.Clamp(angle, 1f, 180f) * 0.5f * Mathf.Deg2Rad);
        return Vector3.Dot(offset / distance, Vector3.forward.RotatedBy(facing)) + 0.0001f >= minimum;
    }
}
