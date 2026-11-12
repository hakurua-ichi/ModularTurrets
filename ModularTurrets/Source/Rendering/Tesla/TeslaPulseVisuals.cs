using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace ModularTurrets;

[StaticConstructorOnStartup]
internal static class TeslaPulseVisuals
{
    internal const int DurationTicks = 32;
    private const int ClusterCount = 96;
    private static readonly BeamVisualExtension Arc = new()
    {
        width = 0.18f, coreWidth = 0.075f,
        coreColor = new Color(0.97f, 1f, 1f, 1f),
        haloColor = new Color(0.55f, 0.82f, 1f, 0.6f)
    };
    private static Material[] opacityMaterials;
    internal static float LineWidth => Arc.width;

    internal static void Spawn(Building_ModularTurret turret, float range)
    {
        if (!turret.Spawned || !float.IsFinite(range) || range <= 0f) return;
        turret.AddTeslaPulseVisual(range);
    }

    internal static void Draw(Pulse pulse, int age, CellRect view)
    {
        // Cull the whole effect bounds, not the turret center. Include line glow.
        float extent = pulse.Range + Arc.width;
        if (pulse.Center.x + extent < view.minX || pulse.Center.x - extent > view.maxX + 1f
            || pulse.Center.z + extent < view.minZ || pulse.Center.z - extent > view.maxZ + 1f) return;
        pulse.UpdateGeometry(age);
        if (opacityMaterials == null)
        {
            opacityMaterials = new Material[8];
            var texture = (Texture2D)BeamVisuals.GetMaterial(Arc).mainTexture;
            for (int i = 0; i < opacityMaterials.Length; i++)
            {
                opacityMaterials[i] = MaterialPool.MatFrom(texture, ShaderDatabase.Transparent,
                    new Color(1f, 1f, 1f, (i + 1) / 8f));
                opacityMaterials[i].enableInstancing = true;
            }
        }
        // Both paths consume the same geometry; only submission grouping changes.
        bool instancing = SystemInfo.supportsInstancing
            && ModularTurretsMod.Instance?.Settings?.TeslaVisualInstancing != false;
        for (int i = 0; i < pulse.Batches.Length; i++)
        {
            var batch = pulse.Batches[i];
            if (batch.Count == 0) continue;
            if (instancing) Graphics.DrawMeshInstanced(MeshPool.plane10, 0, opacityMaterials[i], batch);
            else
                foreach (var matrix in batch)
                    Graphics.DrawMesh(MeshPool.plane10, matrix, opacityMaterials[i], 0);
        }
    }

    internal sealed class Pulse
    {
        internal Map Map;
        internal IntVec3 Position;
        internal Vector3 Center;
        internal float Range;
        internal int Started, Seed;
        internal bool Active;
        internal readonly List<Matrix4x4>[] Batches = new List<Matrix4x4>[8];
        private readonly Action<Vector3, Vector3, float> captureSegment;
        private int cachedPhase = -1;
        internal TeslaPulseGeometry.Output Geometry;
        internal int Generation;

        internal Pulse()
        {
            for (int i = 0; i < Batches.Length; i++) Batches[i] = new List<Matrix4x4>(96);
            captureSegment = CaptureSegment;
        }

        internal void Reset(Map map, IntVec3 position, Vector3 center, float range, int now, int seed)
        {
            Map = map; Position = position; Center = center; Range = range;
            Started = now; Seed = seed; Active = true; cachedPhase = -1;
            Generation++;
        }

        internal void UpdateGeometry(int age)
        {
            int phase = age / 2;
            if (cachedPhase == phase) { TurretGeometryPreDraw.RecordScalar(cacheHit: true); return; }
            TurretGeometryPreDraw.RecordScalar(cacheHit: false);
            foreach (var batch in Batches) batch.Clear();
            Vector3 center = Center;
            center.y = Altitudes.AltitudeFor(AltitudeLayer.MoteOverhead);
            EmitSegments(center, Range, phase * 2, Seed, captureSegment);
            cachedPhase = phase;
        }

        internal bool NeedsGeometry(int age) => cachedPhase != age / 2;

        internal TeslaPulseGeometry.Input Snapshot(int age)
            => new(Center, Range, age / 2 * 2, Seed, Altitudes.AltitudeFor(AltitudeLayer.MoteOverhead),
                Altitudes.AltIncVect * 4f, Arc.width);

        internal void AcceptGeometry(int age) => cachedPhase = age / 2;

        private void CaptureSegment(Vector3 from, Vector3 to, float opacity)
        {
            Vector3 delta = to - from;
            delta.y = 0f;
            float length = delta.magnitude;
            if (length < 0.001f || opacity <= 0f) return;
            int bucket = Math.Clamp(Mathf.CeilToInt(opacity * 8f) - 1, 0, 7);
            Batches[bucket].Add(Matrix4x4.TRS((from + to) * 0.5f + Altitudes.AltIncVect * 4f,
                Quaternion.LookRotation(delta), new Vector3(Arc.width, 1f, length)));
        }
    }

    // Pure geometry shared by rendering and the limited visual-review harness.
    // Dispersed, branching discharges: never a ring or a scaled EMP image.
    internal static void EmitSegments(Vector3 center, float range, int age, int seed,
        Action<Vector3, Vector3, float> emit)
    {
        if (age < 0 || age >= DurationTicks || !float.IsFinite(range) || range <= 0f) return;
        for (int cluster = 0; cluster < ClusterCount; cluster++)
        {
            float radial = Mathf.Sqrt((cluster + 0.3f + Noise(seed, cluster, 0) * 0.4f) / ClusterCount);
            int delay = (int)(radial * 7f);
            int localAge = age - delay;
            if (localAge < 0 || localAge >= 24) continue;
            int generation = localAge / 12, flashAge = localAge % 12;
            float opacity = Mathf.Min(1f, (flashAge + 1f) / 2f) * (1f - Mathf.SmoothStep(0.3f, 1f, flashAge / 11f));
            if (opacity <= 0f) continue;
            // Golden-angle disk sampling fills the interior. The second flash
            // appears elsewhere; only its start time propagates outward.
            float bearing = cluster * 137.508f + Noise(seed, cluster, generation + 10) * 75f;
            float radius = Math.Max(0f, range - Math.Min(0.75f, range * 0.25f)) * radial;
            Vector3 origin = center + Offset(bearing, radius);
            int phase = flashAge / 2 + generation * 10;
            float orientation = Noise(seed, cluster, phase + 30) * 360f;
            for (int arm = 0; arm < 3; arm++)
            {
                int element = cluster * 11 + arm;
                float direction = orientation + arm * 120f + (Noise(seed, element, phase + 40) - 0.5f) * 45f;
                float length = Math.Min(range * 0.23f, 0.35f + Noise(seed, element, phase + 50) * 0.4f);
                Vector3 bend = origin + Offset(direction + (Noise(seed, element, phase + 60) - 0.5f) * 55f, length * 0.48f);
                Vector3 tip = origin + Offset(direction, length);
                emit(origin, bend, opacity);
                emit(bend, tip, opacity);
                // One extra fork gives each discharge an asymmetric branching
                // silhouette instead of an identical star stamp. 7 lines each.
                if (arm == 0)
                    emit(bend, bend + Offset(direction + 65f, length * 0.35f), opacity * 0.8f);
            }
        }
    }

    private static Vector3 Offset(float angle, float distance)
    {
        float radians = angle * Mathf.Deg2Rad;
        return new Vector3(Mathf.Sin(radians) * distance, 0f, Mathf.Cos(radians) * distance);
    }

    // Local integer hashing never reads or changes Verse.Rand's combat state.
    private static float Noise(int seed, int element, int phase)
    {
        unchecked
        {
            uint value = (uint)seed ^ (uint)element * 0x9E3779B9u ^ (uint)phase * 0x85EBCA6Bu;
            value ^= value >> 16; value *= 0x7FEB352Du;
            value ^= value >> 15; value *= 0x846CA68Bu;
            value ^= value >> 16;
            return (value & 0xFFFFFFu) / 16777215f;
        }
    }
}
