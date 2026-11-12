using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace ModularTurrets;

public static class OffMapMortarTargeting
{
    private static Material redRangeMaterial;

    public static void DrawRangeRing(PlanetTile center, int maximum)
    {
        // Reuse the vanilla high-visibility world line without extra draw passes.
        redRangeMaterial ??= MaterialPool.MatFrom(GenDraw.OneSidedLineOpaqueTexPath,
            ShaderDatabase.WorldOverlayAdditiveTwoSided, Color.red, 3590);
        GenDraw.DrawWorldRadiusRing(center, maximum, redRangeMaterial);
    }

    public static void Begin(Building_ModularTurret turret)
    {
        if (CombatCompatibility.Remote != null || turret?.Spawned != true || turret.IsWrecked
            || turret.AttackVerb is not Verb_ModularMortar) return;
        var session = new Session(turret);
        CameraJumper.TryJump(CameraJumper.GetWorldTarget(turret));
        Find.WorldSelector.ClearSelection();
        Find.WorldTargeter.BeginTargeting(session.Select, canTargetTiles: true, closeWorldTabWhenFinished: true,
            onUpdate: session.Draw, extraLabelGetter: session.Label, originForClosest: turret.Map.Tile, showCancelButton: true);
    }

    private sealed class Session
    {
        private readonly Building_ModularTurret turret;
        private readonly PlanetTile origin;
        private readonly Dictionary<PlanetTile, int> distances = new();
        private PlanetLayer cachedLayer;
        private PlanetTile center;
        private int maximum = -1;

        internal Session(Building_ModularTurret turret) { this.turret = turret; origin = turret.Map.Tile; }

        private void EnsureLayer(PlanetLayer layer)
        {
            int range = OffMapMortarRange.Maximum(layer);
            if (cachedLayer == layer && maximum == range) return;
            cachedLayer = layer; maximum = range;
            center = origin.Layer == layer ? origin : layer.GetClosestTile_NewTemp(origin);
            distances.Clear();
            // One bounded census per viewed layer, not one BFS per mouse move/frame.
            center.Layer.Filler.FloodFill(center, tile => true, (tile, distance) =>
            {
                if (distance > maximum) return true;
                distances[tile] = distance;
                return false;
            });
        }

        internal void Draw()
        {
            if (!SourceValid()) { Find.WorldTargeter.StopTargeting(); return; }
            EnsureLayer(PlanetLayer.Selected);
            // Core caches the boundary by center/radius/world seed. The override
            // is read-only; no shared material color or render state is changed.
            DrawRangeRing(center, maximum);
        }

        private bool SourceValid() => turret.Spawned && !turret.IsWrecked && turret.Map.Tile == origin
            && turret.Faction == Faction.OfPlayer && turret.AttackVerb is Verb_ModularMortar;

        private static Map LoadedMap(PlanetTile tile)
        {
            foreach (var map in Find.Maps)
                if (map.Tile == tile && map.Parent?.Spawned == true) return map;
            return null;
        }

        private string Rejection(PlanetTile tile)
        {
            if (!tile.Valid) return L.T("MT_OffMapWorldSelect", OffMapMortarRange.SurfaceMaximum);
            EnsureLayer(tile.Layer);
            if (!distances.ContainsKey(tile)) return L.T("MT_OffMapOutOfRange", maximum);
            if (tile == origin) return L.T("MT_OffMapSameTile");
            if (LoadedMap(tile) == null) return L.T("MT_OffMapNoLoadedMapAtTile");
            return null;
        }

        internal TaggedString Label(GlobalTargetInfo target)
        {
            if (!target.Tile.Valid) return L.T("MT_OffMapWorldSelect", OffMapMortarRange.SurfaceMaximum);
            string rejection = Rejection(target.Tile);
            string distance = distances.TryGetValue(target.Tile, out int value) ? value.ToString() : ">" + maximum;
            string text = L.T("MT_OffMapWorldDistance", distance, maximum) + "\n"
                + (rejection ?? L.T("MT_OffMapWorldReady"));
            return rejection == null ? text : text.Colorize(ColorLibrary.RedReadable);
        }

        internal bool Select(GlobalTargetInfo target)
        {
            if (!SourceValid()) { Find.WorldTargeter.StopTargeting(); return false; }
            string rejection = Rejection(target.Tile);
            if (rejection != null)
            { Messages.Message(rejection, MessageTypeDefOf.RejectInput, false); return false; }
            Map destination = LoadedMap(target.Tile);
            Find.WorldTargeter.StopTargeting();
            ChooseCell(turret, destination);
            return false;
        }
    }

    private static void ChooseCell(Building_ModularTurret turret, Map destination)
    {
        Map source = turret.Map;
        CameraJumper.TryJump(destination.Center, destination, CameraJumper.MovementMode.Pan);
        if (!OffMapGroundScope.ShowGround(destination))
        {
            Current.Game.CurrentMap = source;
            Messages.Message(L.T("MT_OffMapFloorUnavailable"), MessageTypeDefOf.RejectInput, false);
            return;
        }
        Find.Targeter.BeginTargeting(new TargetingParameters { canTargetLocations = true, canTargetPawns = true, canTargetBuildings = true }, target =>
        {
            if (Current.Game.CurrentMap != destination)
            { Messages.Message(L.T("MT_OffMapNeedsLoadedMap"), MessageTypeDefOf.RejectInput, false); return; }
            if (!OffMapMortar.Order(turret, destination, target.Cell, out string reason))
                Messages.Message(reason, MessageTypeDefOf.RejectInput, false);
        }, null, () => { if (Find.Maps.Contains(source)) CameraJumper.TryJump(turret.Position, source, CameraJumper.MovementMode.Pan); }, TexCommand.Attack, false);
    }
}
