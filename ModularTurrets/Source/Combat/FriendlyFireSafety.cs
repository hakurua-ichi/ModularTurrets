using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace ModularTurrets;

internal static class FriendlyFireSafety
{
    // Protect non-hostile visitors and faction-owned animals, except active
    // aggressive mental states. Non-aggressive breaks remain protected.
    internal static bool IsProtectedPawn(Thing thing, Faction source)
        => thing is Pawn pawn && source != null && pawn.Faction != null
            && !pawn.InAggroMentalState && !pawn.Faction.HostileTo(source);

    internal static bool IsProtectedBuilding(Thing thing, Faction source)
        => source != null && thing is Building { Spawned: true } && thing.Faction == source;

    internal static List<Thing> ProtectedPawns(Map map, Faction source, bool protectBuildings = false,
        IntVec3? explosionCenter = null, float explosionRadius = float.PositiveInfinity)
    {
        if (source == null) return null;
        var protectedPawns = new List<Thing>();
        foreach (Pawn pawn in map.mapPawns.AllPawnsSpawned)
            if (IsProtectedPawn(pawn, source)) protectedPawns.Add(pawn);
        if (protectBuildings)
        {
            CellRect? bounds = null;
            if (explosionCenter is { } center && center.InBounds(map) && float.IsFinite(explosionRadius)
                && explosionRadius >= 0f && explosionRadius < Mathf.Max(map.Size.x, map.Size.z))
            {
                int reach = Mathf.CeilToInt(explosionRadius) + 2;
                bounds = CellRect.FromLimits(center.x - reach, center.z - reach, center.x + reach, center.z + reach).ClipInsideMap(map);
            }
            var buildings = source == Faction.OfPlayer ? map.listerBuildings.allBuildingsColonist : map.listerBuildings.allBuildingsNonColonist;
            foreach (Building building in buildings)
                if (IsProtectedBuilding(building, source)
                    && (!bounds.HasValue || bounds.Value.Overlaps(building.OccupiedRect()))) protectedPawns.Add(building);
        }
        return protectedPawns;
    }
}
