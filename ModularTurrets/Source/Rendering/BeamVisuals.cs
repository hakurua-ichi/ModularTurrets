using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace ModularTurrets;

[StaticConstructorOnStartup]
internal static class BeamVisuals
{
    private const int Columns = 64, Rows = 128;
    private static readonly Dictionary<BeamVisualExtension, Material> Materials = new();
    private static MaterialPropertyBlock properties;

    internal static Material GetMaterial(BeamVisualExtension visual)
    {
        if (!Materials.TryGetValue(visual, out Material material))
        {
            var pixels = new Color32[Columns * Rows];
            float coreRatio = visual.coreWidth / visual.width;
            for (int y = 0; y < Rows; y++)
            {
                float along = y / (float)(Rows - 1);
                float cap = Mathf.SmoothStep(0f, 1f, Mathf.Min(along, 1f - along) / 0.015f);
                for (int x = 0; x < Columns; x++)
                {
                    float across = Mathf.Abs(2f * x / (Columns - 1) - 1f);
                    float core = 1f - Mathf.SmoothStep(0f, 1f, across / coreRatio);
                    Color color = Color.Lerp(visual.haloColor, visual.coreColor, core);
                    color.a *= (1f - Mathf.SmoothStep(0f, 1f, (across - coreRatio) / (1f - coreRatio))) * cap;
                    pixels[y * Columns + x] = color;
                }
            }
            var texture = new Texture2D(Columns, Rows, TextureFormat.RGBA32, false)
            {
                name = "MT_WeaponBeamGradient",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            material = MaterialPool.MatFrom(texture, ShaderDatabase.Transparent, Color.white);
            Materials.Add(visual, material);
        }

        return material;
    }

    internal static void Draw(Vector3 from, Vector3 to, BeamVisualExtension visual, float remaining)
    {
        if (!visual.Valid || !float.IsFinite(remaining) || remaining <= 0f) return;
        Vector3 direction = to - from;
        direction.y = 0f;
        float length = direction.magnitude;
        if (!float.IsFinite(length) || length < 0.001f || !float.IsFinite(from.y) || !float.IsFinite(to.y)) return;

        Material material = GetMaterial(visual);

        // A single instant beam: a bright center, a soft halo, then a tick-based fade.
        // Use a per-draw tint so fading never mutates a material shared by other shots.
        properties ??= new MaterialPropertyBlock();
        properties.SetColor("_Color", new Color(1f, 1f, 1f, Mathf.Clamp01(remaining)));
        Vector3 center = (from + to) * 0.5f + Altitudes.AltIncVect * 4f;
        Graphics.DrawMesh(MeshPool.plane10,
            Matrix4x4.TRS(center, Quaternion.LookRotation(direction), new Vector3(visual.width, 1f, length)),
            material, 0, null, 0, properties);
    }
}
