using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace ModularTurrets;

public sealed partial class Building_ModularTurret
{
    private TeslaPulseVisuals.Pulse[] teslaPulses;

    internal void AddTeslaPulseVisual(float range)
    {
        int now = Find.TickManager.TicksGame;
        teslaPulses ??= new[] { new TeslaPulseVisuals.Pulse(), new TeslaPulseVisuals.Pulse() };
        TeslaPulseVisuals.Pulse available = null;
        foreach (var pulse in teslaPulses)
        {
            if (!TeslaPulseStillActive(pulse, now)) pulse.Active = false;
            if (!pulse.Active && available == null) available = pulse;
        }
        // Keep existing afterglows to completion under extreme firing rates.
        // Skipping a cosmetic pulse never suppresses the corresponding attack.
        if (available == null) return;
        Vector3 center = DrawPos;
        // Do not draw beyond the furthest map corner at extreme upgrade ranges.
        float x = Math.Max(center.x, Map.Size.x - center.x);
        float z = Math.Max(center.z, Map.Size.z - center.z);
        float visibleRange = Math.Min(range, Mathf.Sqrt(x * x + z * z));
        available.Reset(Map, Position, center, visibleRange, now, Gen.HashCombineInt(thingIDNumber, now));
    }

    private bool TeslaPulseStillActive(TeslaPulseVisuals.Pulse pulse, int now)
        => pulse.Active && now >= pulse.Started && now - pulse.Started < TeslaPulseVisuals.DurationTicks
            && pulse.Map == Map && pulse.Position == Position;

    private void DrawTeslaPulseVisuals()
    {
        if (teslaPulses == null) return;
        int now = Find.TickManager.TicksGame;
        var view = Find.CameraDriver.CurrentViewRect;
        foreach (var pulse in teslaPulses)
        {
            if (!TeslaPulseStillActive(pulse, now)) { pulse.Active = false; continue; }
            TeslaPulseVisuals.Draw(pulse, now - pulse.Started, view);
        }
    }
}
