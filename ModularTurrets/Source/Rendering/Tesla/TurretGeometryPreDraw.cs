using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using UnityEngine;
using Verse;
using MeshDrawSubmission = Waylith.Rendering.MeshDrawSubmission;

namespace ModularTurrets;

// Core schedules and joins PreDraw jobs before the existing scalar draw path.
internal static class TurretGeometryPreDraw
{
    private static int enabled = 1, batchStarted;
    private static int retryRequested;
    private static int preparedFrame = -1, preparedMapId = -1, contextGeneration;
    private static int visibilityFrame = -1, visibilityMapId = -1, visibilityGeneration;
    private static int previousVisiblePulseCount, currentVisiblePulseCount;
    internal static int MinimumPreparedPulses = 8;
    internal static int PendingPulseCount, PreparedPulseCount;
    internal static bool Faulted;
    internal static bool DiagnosticsEnabled = false;
    private static readonly object diagnosticsLock = new();
    private static readonly HashSet<int> diagnosticThreads = new();
    private static readonly long[] fallbackReasons = new long[7];
    private static long preparedAttempts, startedWork, finishedWork, validWork, appliedWork;
    private static long scalarRebuilds, cacheHits, duplicateCallbacks, replacedPreparations, computationTicks;
    private static string lastError;
    private static int diagnosticEpoch = 1;
    [ThreadStatic] private static int registeredDiagnosticEpoch;

    internal enum Reason { ScopeChanged, InputChanged, ThresholdOrPhaseSkipped, WorkerError, MissingCompletion, LatePrepare, OutputReclaimed }

    internal static void RecordScalar(bool cacheHit)
    {
        if (!DiagnosticsEnabled) return;
        if (cacheHit) cacheHits++; else scalarRebuilds++;
    }

    internal static void RecordFallback(Reason reason, int count = 1)
    {
        if (DiagnosticsEnabled) Interlocked.Add(ref fallbackReasons[(int)reason], count);
    }

    internal static void RecordApplied()
    {
        if (DiagnosticsEnabled) appliedWork++;
    }

    private static void RecordStart(int threadId)
    {
        Interlocked.Increment(ref startedWork);
        int epoch = Volatile.Read(ref diagnosticEpoch);
        if (registeredDiagnosticEpoch == epoch) return;
        lock (diagnosticsLock) diagnosticThreads.Add(threadId);
        registeredDiagnosticEpoch = epoch;
    }

    // Call only between joined draw passes; this never changes pending work.
    internal static void ResetDiagnostics()
    {
        preparedAttempts = startedWork = finishedWork = validWork = appliedWork = 0;
        scalarRebuilds = cacheHits = duplicateCallbacks = replacedPreparations = computationTicks = 0;
        Array.Clear(fallbackReasons);
        lock (diagnosticsLock) { diagnosticThreads.Clear(); Interlocked.Increment(ref diagnosticEpoch); }
        lastError = null;
    }

    internal static object Snapshot()
    {
        int[] threads;
        lock (diagnosticsLock) { threads = new int[diagnosticThreads.Count]; diagnosticThreads.CopyTo(threads); }
        return new
        {
            Enabled, DiagnosticsEnabled, Faulted, MinimumPreparedPulses, PendingPulseCount, PreparedPulseCount,
            frame = preparedFrame, mapId = preparedMapId, contextGeneration,
            visiblePulses = new { frame = visibilityFrame, mapId = visibilityMapId, previous = previousVisiblePulseCount, current = currentVisiblePulseCount },
            preparedAttempts, startedWork, finishedWork, validWork, appliedWork, scalarRebuilds, cacheHits,
            duplicateCallbacks, replacedPreparations, threadIds = threads, snapshotThreadId = Environment.CurrentManagedThreadId,
            summedComputationMs = Volatile.Read(ref computationTicks) * 1000d / Stopwatch.Frequency,
            timingScope = "Sum of pure computation elapsed across callbacks, including main-thread stealing; not critical path or GPU time",
            fallback = new
            {
                scopeChanged = fallbackReasons[0], inputChanged = fallbackReasons[1], thresholdOrPhaseSkipped = fallbackReasons[2],
                workerError = fallbackReasons[3], missingCompletion = fallbackReasons[4], latePrepare = fallbackReasons[5], outputReclaimed = fallbackReasons[6]
            },
            lastError
        };
    }

    internal static bool Enabled
    {
        get => Volatile.Read(ref enabled) != 0;
        set
        {
            bool previous = Volatile.Read(ref enabled) != 0;
            if (value != previous)
            {
                visibilityFrame = visibilityMapId = -1;
                previousVisiblePulseCount = currentVisiblePulseCount = 0;
            }
            if (value && !previous) Faulted = false;
            Volatile.Write(ref enabled, value ? 1 : 0);
        }
    }

    // Main-thread history counts visible active pulses even on geometry-cache hits.
    // A new map, skipped frame or first enabled frame starts with scalar geometry.
    internal static bool BeginVisibilityFrame(int frame, int mapId, out int generation)
    {
        if (frame != visibilityFrame || mapId != visibilityMapId)
        {
            previousVisiblePulseCount = mapId == visibilityMapId && frame == visibilityFrame + 1
                ? currentVisiblePulseCount : 0;
            currentVisiblePulseCount = 0;
            visibilityFrame = frame; visibilityMapId = mapId;
            visibilityGeneration++;
        }
        generation = visibilityGeneration;
        return previousVisiblePulseCount >= Math.Max(1, MinimumPreparedPulses);
    }

    internal static void RegisterVisiblePulses(int count) => currentVisiblePulseCount += count;

    internal static bool CanPrepare(int frame, int mapId)
        => frame != preparedFrame || mapId != preparedMapId || PendingPulseCount == 0
            || Volatile.Read(ref batchStarted) == 0;

    internal static int RegisterPrepared(int frame, int mapId, int count)
    {
        if (frame != preparedFrame || mapId != preparedMapId || PendingPulseCount == 0)
        {
            preparedFrame = frame; preparedMapId = mapId;
            PendingPulseCount = PreparedPulseCount = 0;
            Volatile.Write(ref batchStarted, 0);
            contextGeneration++;
        }
        PendingPulseCount += count;
        PreparedPulseCount = Math.Max(PreparedPulseCount, PendingPulseCount);
        if (DiagnosticsEnabled) preparedAttempts += count;
        return contextGeneration;
    }

    internal static bool CanRun(int generation)
        => Enabled && !Faulted && generation == contextGeneration
            && Volatile.Read(ref PendingPulseCount) >= Math.Max(1, MinimumPreparedPulses);

    internal static void Fail(Exception error)
    {
        if (Faulted) return;
        Faulted = true;
        lastError = error.GetType().Name + ": " + error.Message;
        try { Log.Warning("[Modular Turrets] Tesla pre-draw disabled; using scalar geometry. " + error.Message); }
        catch { /* Diagnostics must not replace the original draw path. */ }
    }

    internal struct Work
    {
        internal bool Ready;
        internal int PulseGeneration, OutputGeneration, Started;
        internal TeslaPulseGeometry.Input Input;
        internal TeslaPulseGeometry.Output Output;

        internal void Execute()
        {
            if (Output.Generation != OutputGeneration) { RecordFallback(Reason.OutputReclaimed); return; }
            Output.ExecutionThreadId = Environment.CurrentManagedThreadId;
            Output.ComputeCount++;
            bool track = DiagnosticsEnabled;
            if (track) RecordStart(Output.ExecutionThreadId);
            long begin = track ? Stopwatch.GetTimestamp() : 0;
            try
            {
                Output.Compute(in Input);
                Output.CompletedGeneration = OutputGeneration;
                Volatile.Write(ref Output.Valid, 1);
            }
            catch (Exception error)
            {
                Output.Error = error;
                Output.CompletedGeneration = OutputGeneration;
                Volatile.Write(ref Output.Valid, 0);
            }
            finally
            {
                if (track)
                {
                    Interlocked.Add(ref computationTicks, Stopwatch.GetTimestamp() - begin);
                    Interlocked.Increment(ref finishedWork);
                    if (Output.Valid == 1) Interlocked.Increment(ref validWork);
                }
            }
        }
    }

    internal sealed class Preparation(int count)
    {
        internal readonly Work[] Items = new Work[count];
        internal int Ready, Attempted, Frame, Tick, MapId, ContextGeneration;
        internal int RegisteredCount, BatchPulseCount, AppliedCount, FallbackCount;
        private int executionClaim;

        internal void ReleaseRegistration()
        {
            if (ContextGeneration == contextGeneration) PendingPulseCount -= RegisteredCount;
            RegisteredCount = 0;
        }

        internal void Clear()
        {
            if (DiagnosticsEnabled && RegisteredCount != 0 && Volatile.Read(ref Attempted) == 0) replacedPreparations++;
            Volatile.Write(ref Ready, 0);
            ReleaseRegistration();
            Interlocked.Exchange(ref executionClaim, 0);
            Attempted = BatchPulseCount = AppliedCount = FallbackCount = 0;
            for (int i = 0; i < Items.Length; i++) Items[i].Ready = false;
        }

        internal void Execute()
        {
            if (Volatile.Read(ref Ready) == 0 || !CanRun(ContextGeneration)) return;
            if (Interlocked.CompareExchange(ref executionClaim, 1, 0) != 0)
            {
                if (DiagnosticsEnabled) Interlocked.Increment(ref duplicateCallbacks);
                return;
            }
            if (Volatile.Read(ref batchStarted) == 0) Interlocked.Exchange(ref batchStarted, 1);
            BatchPulseCount = Volatile.Read(ref PendingPulseCount);
            Volatile.Write(ref Attempted, 1);
            for (int i = 0; i < Items.Length; i++)
                if (Items[i].Ready) Items[i].Execute();
        }
    }

    internal static bool CurrentContext(int generation) => generation == contextGeneration;

    internal static void RequestRetry() => Volatile.Write(ref retryRequested, 1);

    // Settings are applied on the next main-thread preparation, after workers join.
    internal static void ApplyRequestedRetry()
    {
        if (Interlocked.Exchange(ref retryRequested, 0) == 0) return;
        Faulted = false;
        visibilityFrame = visibilityMapId = -1;
        previousVisiblePulseCount = currentVisiblePulseCount = 0;
    }
}
