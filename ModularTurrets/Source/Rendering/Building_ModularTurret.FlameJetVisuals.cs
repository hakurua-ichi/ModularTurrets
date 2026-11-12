using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace ModularTurrets;

public sealed partial class Building_ModularTurret
{
    // Cosmetic only: intentionally not Scribed. Loading drops a <=8-tick tail.
    private readonly List<FlameJetVisuals.Tile> pendingFlameTiles = new();
    private int nextFlameTileWaveTick;

    internal void QueueFlameTileVisuals(Vector3 muzzle, float range, float angle)
    {
        if (!Spawned || Destroyed || !float.IsFinite(range) || range <= 0f || !float.IsFinite(angle)) return;
        int tick = Find.TickManager.TicksGame;
        if (tick < nextFlameTileWaveTick) return;
        nextFlameTileWaveTick = (int)Math.Min(int.MaxValue, (long)tick + FlameJetVisuals.MinimumWaveInterval);
        Vector3 forward = Vector3.forward.RotatedBy(AimAngle), side = Vector3.right.RotatedBy(AimAngle);
        for (int row = 0; row < 5; row++)
        {
            // Compact visual lattice, not integer map-cell snapping. This keeps
            // 1/3/5/7/9 flames distinct and the wide final row inside base range 6.
            // Upgraded range does not enlarge these near-muzzle flame tiles.
            for (int column = -row; column <= row; column++)
            {
                Vector3 position = muzzle + (forward * (row + 1) + side * column) * FlameJetVisuals.TileSpacing;
                if (!position.ShouldSpawnMotesAt(Map)) continue;
                int seed = Gen.HashCombineInt(Gen.HashCombineInt(thingIDNumber, tick), row * 11 + column + 5);
                int due = (int)Math.Min(int.MaxValue, (long)tick + row * FlameJetVisuals.RowDelay);
                var tile = new FlameJetVisuals.Tile(this, muzzle, position, range, angle, due, seed);
                if (!FlameJetVisuals.Visible(this, tile)) continue;
                if (row == 0) FlameJetVisuals.Emit(this, tile);
                else if (pendingFlameTiles.Count < FlameJetVisuals.MaximumPendingTiles) pendingFlameTiles.Add(tile);
            }
        }
    }

    private void TickFlameTileVisuals()
    {
        if (pendingFlameTiles.Count == 0) return;
        int tick = Find.TickManager.TicksGame;
        for (int i = pendingFlameTiles.Count - 1; i >= 0; i--)
        {
            var tile = pendingFlameTiles[i];
            if (!Spawned || Destroyed || Map != tile.Map || Position != tile.SourceCell || tick - (long)tile.Due > 12)
            {
                pendingFlameTiles.RemoveAt(i);
                continue;
            }
            if (tile.Due > tick) continue;
            FlameJetVisuals.Emit(this, tile);
            pendingFlameTiles.RemoveAt(i);
        }
    }
}
