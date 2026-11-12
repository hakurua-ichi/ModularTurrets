using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using RimWorld;
using UnityEngine;
using Verse;

namespace ModularTurrets;

public static class MuzzleGeometry
{
    public static Vector3 ImagePoint(Vector3 anchor, Vector2 pivot, Vector2 imageSize, float angle, bool flipX, Vector2 uv)
        => anchor + new Vector3((uv.x - pivot.x) * imageSize.x * (flipX ? -1f : 1f), 0f,
            (pivot.y - uv.y) * imageSize.y).RotatedBy(angle);
}
