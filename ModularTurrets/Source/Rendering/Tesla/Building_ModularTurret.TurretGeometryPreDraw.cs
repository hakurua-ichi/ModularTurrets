using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using UnityEngine;
using Verse;
using MeshDrawSubmission = Waylith.Rendering.MeshDrawSubmission;

namespace ModularTurrets;

public sealed partial class Building_ModularTurret
{
    private TurretGeometryPreDraw.Preparation teslaGeometryPreparation;
    private int teslaVisibilityGeneration = -1;

    public override void DynamicDrawPhaseAt(DrawPhase phase, Vector3 drawLoc, bool flip = false)
    {
        if (phase == DrawPhase.ParallelPreDraw)
            teslaGeometryPreparation?.Execute();
        else
        {
            try
            {
                if (phase == DrawPhase.EnsureInitialized)
                {
                    if (teslaPulses != null || teslaGeometryPreparation != null) PrepareTeslaGeometry();
                }
                else if (phase == DrawPhase.Draw && teslaGeometryPreparation != null)
                    ApplyTeslaGeometry(teslaGeometryPreparation);
            }
            catch (Exception error)
            {
                teslaGeometryPreparation?.Clear();
                TurretGeometryPreDraw.Fail(error);
            }
        }
        // Base calls the existing DrawAt once; other phases are no-ops for this base.
        base.DynamicDrawPhaseAt(phase, drawLoc, flip);
    }

    private void PrepareTeslaGeometry()
    {
        // Unity/map access stays here. Workers only receive the captured values;
        // changing a setting must not replace work after the parallel pass starts.
        if (!UnityData.IsInMainThread) return;
        TurretGeometryPreDraw.ApplyRequestedRetry();
        if (!TurretGeometryPreDraw.Enabled || TurretGeometryPreDraw.Faulted
            || ModularTurretsMod.Instance?.Settings?.TeslaParallelGeometry == false)
        {
            teslaGeometryPreparation?.Clear();
            return;
        }
        int frame = Time.frameCount;
        // A late nested prepare cannot replace a batch whose workers have started.
        if (Spawned && !TurretGeometryPreDraw.CanPrepare(frame, Map.uniqueID))
        {
            TurretGeometryPreDraw.RecordFallback(TurretGeometryPreDraw.Reason.LatePrepare);
            return;
        }
        teslaGeometryPreparation?.Clear();
        if (teslaPulses == null || !Spawned || IsWrecked || Map != Find.CurrentMap || MeshDrawSubmission.IsCapturing) return;
        int now = Find.TickManager.TicksGame;
        CellRect view = Find.CameraDriver.CurrentViewRect;
        bool prepare = TurretGeometryPreDraw.BeginVisibilityFrame(frame, Map.uniqueID, out int visibleGeneration);
        int count = 0, visibleCount = 0;
        for (int i = 0; i < teslaPulses.Length; i++)
        {
            var pulse = teslaPulses[i];
            if (!TeslaPulseStillActive(pulse, now)) continue;
            float extent = pulse.Range + TeslaPulseVisuals.LineWidth;
            if (pulse.Center.x + extent < view.minX || pulse.Center.x - extent > view.maxX + 1f
                || pulse.Center.z + extent < view.minZ || pulse.Center.z - extent > view.maxZ + 1f) continue;
            visibleCount++;
            if (!prepare || !pulse.NeedsGeometry(now - pulse.Started)) continue;
            teslaGeometryPreparation ??= new TurretGeometryPreDraw.Preparation(teslaPulses.Length);
            var output = pulse.Geometry ??= new TeslaPulseGeometry.Output(pulse.Batches);
            output.Generation++;
            output.Error = null;
            output.CompletedGeneration = -1;
            output.ExecutionThreadId = output.ComputeCount = 0;
            Volatile.Write(ref output.Valid, 0);
            teslaGeometryPreparation.Items[i] = new TurretGeometryPreDraw.Work
            {
                Ready = true, PulseGeneration = pulse.Generation, OutputGeneration = output.Generation,
                Started = pulse.Started, Input = pulse.Snapshot(now - pulse.Started), Output = output
            };
            count++;
        }
        if (teslaVisibilityGeneration != visibleGeneration)
        {
            TurretGeometryPreDraw.RegisterVisiblePulses(visibleCount);
            teslaVisibilityGeneration = visibleGeneration;
        }
        if (count == 0) return;
        var preparation = teslaGeometryPreparation;
        preparation.Frame = frame; preparation.Tick = now; preparation.MapId = Map.uniqueID;
        preparation.ContextGeneration = TurretGeometryPreDraw.RegisterPrepared(frame, preparation.MapId, count);
        preparation.RegisteredCount = count;
        Volatile.Write(ref preparation.Ready, 1);
    }

    private void ApplyTeslaGeometry(TurretGeometryPreDraw.Preparation preparation)
    {
        if (!UnityData.IsInMainThread || Volatile.Read(ref preparation.Ready) == 0) return;
        Volatile.Write(ref preparation.Ready, 0);
        try
        {
            // Threshold was decided during the joined worker phase. Draw consumes
            // registrations, so rechecking its shrinking count here would be wrong.
            if (!TurretGeometryPreDraw.Enabled || TurretGeometryPreDraw.Faulted || teslaPulses == null
                || !Spawned || IsWrecked || Map != Find.CurrentMap || MeshDrawSubmission.IsCapturing
                || preparation.Frame != Time.frameCount || preparation.Tick != Find.TickManager.TicksGame
                || preparation.MapId != Map.uniqueID || !TurretGeometryPreDraw.CurrentContext(preparation.ContextGeneration))
            {
                preparation.FallbackCount = preparation.RegisteredCount;
                TurretGeometryPreDraw.RecordFallback(TurretGeometryPreDraw.Reason.ScopeChanged, preparation.RegisteredCount);
                return;
            }
            for (int i = 0; i < preparation.Items.Length && i < teslaPulses.Length; i++)
            {
                ref var work = ref preparation.Items[i];
                if (!work.Ready) continue;
                var pulse = teslaPulses[i];
                int age = preparation.Tick - pulse.Started;
                if (!TeslaPulseStillActive(pulse, preparation.Tick) || pulse.Generation != work.PulseGeneration
                    || pulse.Started != work.Started || pulse.Seed != work.Input.Seed || pulse.Range != work.Input.Range
                    || !pulse.Center.Equals(work.Input.Center) || age / 2 * 2 != work.Input.Age)
                {
                    preparation.FallbackCount++;
                    TurretGeometryPreDraw.RecordFallback(TurretGeometryPreDraw.Reason.InputChanged);
                    continue;
                }
                if (Volatile.Read(ref preparation.Attempted) == 0 || TurretGeometryPreDraw.Faulted)
                {
                    preparation.FallbackCount++;
                    TurretGeometryPreDraw.RecordFallback(TurretGeometryPreDraw.Reason.ThresholdOrPhaseSkipped);
                    continue;
                }
                if (Volatile.Read(ref work.Output.Valid) == 1 && work.Output.Generation == work.OutputGeneration
                    && work.Output.CompletedGeneration == work.OutputGeneration)
                {
                    pulse.AcceptGeometry(age);
                    preparation.AppliedCount++;
                    TurretGeometryPreDraw.RecordApplied();
                }
                else
                {
                    preparation.FallbackCount++;
                    TurretGeometryPreDraw.RecordFallback(work.Output.Error == null
                        ? TurretGeometryPreDraw.Reason.MissingCompletion : TurretGeometryPreDraw.Reason.WorkerError);
                    TurretGeometryPreDraw.Fail(work.Output.Error ?? new InvalidOperationException("Tesla pre-draw output was not completed."));
                }
            }
        }
        finally { preparation.ReleaseRegistration(); }
    }
}
