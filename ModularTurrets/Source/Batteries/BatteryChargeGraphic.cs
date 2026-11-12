using System;
using System.Runtime.CompilerServices;
using RimWorld;
using UnityEngine;
using Verse;

namespace ModularTurrets.Batteries;

[StaticConstructorOnStartup]
internal static class BatteryChargeGraphic
{
    // Empty glass [284,501,440,1674) in the approved body's [67,66,657,2107).
    private const float Width = Graphic_ModularBattery.ModuleWidth * 156f / 590f;
    private const float Height = 3f * 1173f / 2041f;
    private const float Bottom = 1.5f - 3f * (1674f - 66f) / 2041f;
    private const int Levels = 256;
    private static readonly Mesh[] Fills = new Mesh[Levels + 1];
    private static Material glass;

    internal static void Draw(Vector3 center, Rot4 rot, float fraction)
    {
        if (!(fraction > 0f)) return;
        // Distinct UVs reveal the same pill from its bottom; scaling the
        // entire pill would incorrectly round the moving liquid surface.
        int level = Math.Clamp(Mathf.RoundToInt(fraction * Levels), 1, Levels);
        float height = (float)level / Levels * Height;
        Mesh mesh = Fills[level];
        if (mesh == null)
        {
            mesh = MeshMakerPlanes.NewPlaneMesh(new Vector2(Width, height), false, false, false);
            float top = (float)level / Levels;
            mesh.uv = new[] { Vector2.zero, new Vector2(0f, top), new Vector2(1f, top), Vector2.right };
            Fills[level] = mesh;
        }
        glass ??= MaterialPool.MatFrom("ModularTurrets/Batteries/ChargeGlass", ShaderDatabase.Cutout);
        Quaternion q = rot.AsQuat;
        Vector3 at = center + q * new Vector3(0f, Altitudes.AltInc * 2f, Bottom + height * 0.5f);
        Graphics.DrawMesh(mesh, at, q, glass, 0);
    }
}
