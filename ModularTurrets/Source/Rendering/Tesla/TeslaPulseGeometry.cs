using System;
using System.Collections.Generic;
using UnityEngine;

namespace ModularTurrets;

// Capture game-derived values on the main thread. Workers receive no game object.
internal static class TeslaPulseGeometry
{
    internal readonly struct Input(Vector3 center, float range, int age, int seed, float altitude, Vector3 lift, float width)
    {
        internal readonly Vector3 Center = center, Lift = lift;
        internal readonly float Range = range, Altitude = altitude, Width = width;
        internal readonly int Age = age, Seed = seed;
    }

    // One pulse owns these existing lists. ParallelPreDraw completes before Draw;
    // Graphics copies their matrices into owned storage when submitting them.
    internal sealed class Output
    {
        private readonly List<Matrix4x4>[] batches;
        private readonly Action<Vector3, Vector3, float> captureSegment;
        private float width;
        private Vector3 lift;
        internal int Generation, CompletedGeneration, Valid, ExecutionThreadId, ComputeCount;
        internal Exception Error;

        internal Output(List<Matrix4x4>[] batches)
        {
            this.batches = batches;
            captureSegment = CaptureSegment;
        }

        internal void Compute(in Input input)
        {
            foreach (var batch in batches) batch.Clear();
            width = input.Width; lift = input.Lift;
            Vector3 center = input.Center;
            center.y = input.Altitude;
            TeslaPulseVisuals.EmitSegments(center, input.Range, input.Age, input.Seed, captureSegment);
        }

        private void CaptureSegment(Vector3 from, Vector3 to, float opacity)
        {
            Vector3 delta = to - from;
            delta.y = 0f;
            float length = delta.magnitude;
            if (length < 0.001f || opacity <= 0f) return;
            int bucket = Math.Clamp(Mathf.CeilToInt(opacity * 8f) - 1, 0, 7);
            batches[bucket].Add(Matrix4x4.TRS((from + to) * 0.5f + lift,
                Quaternion.LookRotation(delta), new Vector3(width, 1f, length)));
        }
    }
}
