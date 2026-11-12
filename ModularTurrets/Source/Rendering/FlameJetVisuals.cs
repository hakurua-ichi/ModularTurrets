using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace ModularTurrets;

// Fire-spew-shaped visuals only. Damage, IFF and direct ignition stay in the
// existing cone attack; this class never creates Fire, filth or explosions.
internal static class FlameJetVisuals
{
    private static FleckDef flameBlast;
    internal const int RowDelay = 2, MinimumWaveInterval = 6, MaximumPendingTiles = 50;
    internal const float TileSpacing = 0.7f, TileSize = 0.9f;

    internal readonly struct Tile
    {
        internal readonly Map Map;
        internal readonly IntVec3 SourceCell;
        internal readonly Vector3 Origin, Muzzle, Position;
        internal readonly float Facing, Range, Angle;
        internal readonly int Due, Seed;

        internal Tile(Building_ModularTurret turret, Vector3 muzzle, Vector3 position, float range, float angle, int due, int seed)
        {
            Map = turret.Map; SourceCell = turret.Position;
            Origin = turret.DrawPos; Muzzle = muzzle; Position = position; Facing = turret.AimAngle;
            Range = range; Angle = angle; Due = due; Seed = seed;
        }
    }

    internal static void SpawnCone(Building_ModularTurret turret, Vector3 start, float range, float angle)
        => turret.QueueFlameTileVisuals(start, range, angle);

    private static bool PointVisible(Building_ModularTurret turret, Tile tile, Vector3 point)
    {
        IntVec3 cell = point.ToIntVec3();
        var footprint = turret.OccupiedRect();
        if (!cell.InBounds(tile.Map) || !FlameConeGeometry.Contains(tile.Origin, tile.Facing, tile.Angle, tile.Range, point)
            || !footprint.Contains(cell) && !cell.CanBeSeenOverFast(tile.Map)) return false;
        return GenSight.LineOfSight(tile.SourceCell, cell, tile.Map, footprint, CellRect.SingleCell(cell))
            && GenSight.LineOfSight(tile.Muzzle.ToIntVec3(), cell, tile.Map, footprint, CellRect.SingleCell(cell));
    }

    internal static bool Visible(Building_ModularTurret turret, Tile tile)
    {
        if (!turret.Spawned || turret.Destroyed || turret.Map != tile.Map || turret.Position != tile.SourceCell) return false;
        const float half = TileSize * 0.5f;
        // Include the whole square, not only its centre. A neighbouring wall,
        // map edge or range boundary cannot be crossed by the sprite's corners.
        return PointVisible(turret, tile, tile.Position)
            && PointVisible(turret, tile, tile.Position + new Vector3(-half, 0f, -half))
            && PointVisible(turret, tile, tile.Position + new Vector3(-half, 0f, half))
            && PointVisible(turret, tile, tile.Position + new Vector3(half, 0f, -half))
            && PointVisible(turret, tile, tile.Position + new Vector3(half, 0f, half));
    }

    internal static void Emit(Building_ModularTurret turret, Tile tile)
    {
        // Recheck walls and ownership when the delayed row actually appears.
        if (!tile.Position.ShouldSpawnMotesAt(tile.Map) || !Visible(turret, tile)) return;
        // Reuse the actual cell flash from vanilla flame explosions; emit only
        // its visual, never an Explosion or its secondary damage/fire logic.
        flameBlast ??= DamageDefOf.Flame.explosionCellFleck;
        Rand.PushState(tile.Seed);
        try
        {
            var data = FleckMaker.GetDataStatic(tile.Position, tile.Map, flameBlast);
            data.rotation = 90f * Rand.RangeInclusive(0, 3);
            data.exactScale = new Vector3(TileSize, 1f, TileSize);
            float distance = (tile.Position - tile.Origin).Yto0().magnitude;
            data.instanceColor = Color.Lerp(DamageDefOf.Flame.explosionColorCenter,
                DamageDefOf.Flame.explosionColorEdge, Mathf.Clamp01(distance / tile.Range));
            data.velocitySpeed = 0f;
            tile.Map.flecks.CreateFleck(data);
        }
        finally { Rand.PopState(); }
    }
}
