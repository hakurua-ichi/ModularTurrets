using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace ModularTurrets;

public static class ModuleBeamRenderer
{
    private const int Columns = 8, Rows = 128;
    private static readonly Dictionary<(Color color, float power), Material> Materials = new();

    public static bool TryGetSegment(PartBeam beam, Vector3 anchor, Vector2 pivot, Vector2 imageSize,
        float angle, bool flipX, out Vector3 start, out Vector3 end)
    {
        start = end = anchor;
        if (beam == null || !float.IsFinite(beam.length) || beam.length <= 0f
            || !float.IsFinite(beam.width) || beam.width <= 0f || !float.IsFinite(beam.angle)
            || !float.IsFinite(beam.start.x) || !float.IsFinite(beam.start.y)) return false;
        float mirror = flipX ? -1f : 1f;
        Vector3 localStart = new((beam.start.x - pivot.x) * imageSize.x * mirror, 0f,
            (pivot.y - beam.start.y) * imageSize.y);
        start = anchor + localStart.RotatedBy(angle);
        float radians = beam.angle * Mathf.Deg2Rad;
        Vector3 direction = new(Mathf.Sin(radians) * mirror, 0f, Mathf.Cos(radians));
        end = start + direction.RotatedBy(angle) * beam.length;
        return true;
    }

    public static byte AlphaAt(PartBeam beam, int row)
    {
        double t = Math.Clamp(row, 0, Rows - 1) / (double)(Rows - 1);
        double alpha = 255d * beam.color.a * Math.Pow(1d - t, beam.fadePower);
        // Match the preview's positive Math.round at the same 128 sample points.
        return (byte)Math.Clamp(Math.Floor(alpha + 0.5d), 0d, 255d);
    }

    public static void Draw(PartBeam beam, Vector3 anchor, Vector2 pivot, Vector2 imageSize, float angle, bool flipX)
    {
        if (!TryGetSegment(beam, anchor, pivot, imageSize, angle, flipX, out Vector3 start, out Vector3 end)
            || !float.IsFinite(beam.fadePower) || beam.fadePower <= 0f || !float.IsFinite(beam.color.a) || beam.color.a <= 0f
            || !float.IsFinite(beam.color.r) || !float.IsFinite(beam.color.g) || !float.IsFinite(beam.color.b)) return;
        var key = (beam.color, beam.fadePower);
        if (!Materials.TryGetValue(key, out Material material))
        {
            var pixels = new Color32[Columns * Rows];
            byte Channel(float value) => (byte)Math.Clamp(Math.Floor(Math.Clamp(value, 0f, 1f) * 255d + 0.5d), 0d, 255d);
            byte r = Channel(beam.color.r), g = Channel(beam.color.g), b = Channel(beam.color.b);
            for (int y = 0; y < Rows; y++)
            {
                byte alpha = AlphaAt(beam, y);
                for (int x = 0; x < Columns; x++) pixels[y * Columns + x] = new Color32(r, g, b, alpha);
            }
            var texture = new Texture2D(Columns, Rows, TextureFormat.RGBA32, false)
            {
                name = "MT_ModuleBeamGradient",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            material = MaterialPool.MatFrom(texture, ShaderDatabase.Transparent, Color.white);
            Materials.Add(key, material);
        }
        // plane10 V=0 is its -Z edge: opaque at start, transparent at +Z/end.
        Vector3 center = (start + end) * 0.5f + Altitudes.AltIncVect * 0.25f;
        Graphics.DrawMesh(MeshPool.plane10,
            Matrix4x4.TRS(center, Quaternion.LookRotation(end - start), new Vector3(beam.width, 1f, beam.length)), material, 0);
    }
}
