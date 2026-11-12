using System;
using System.Collections.Generic;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace ModularTurrets;

public static class OffMapMortarRange
{
    public static int SurfaceMaximum
        => Math.Max(1, DefDatabase<OffMapMortarSettingsDef>.GetNamedSilentFail("MT_OffMapMortarSettings")?.maxWorldRange ?? 62);

    // Use the same planet-layer scaling and rounding as CompLaunchable's cap.
    public static int Maximum(PlanetLayer layer)
    {
        float factor = layer?.Def.rangeDistanceFactor ?? 1f;
        return float.IsFinite(factor) && factor > 0f ? Math.Max(0, Mathf.RoundToInt(SurfaceMaximum / factor)) : 0;
    }

    public static bool Within(PlanetTile source, PlanetTile destination)
        => Within(source, destination, null);

    internal static bool Within(PlanetTile source, PlanetTile destination, OffMapMortarMission mission)
    {
        if (!source.Valid || !destination.Valid) return false;
        int maximum = Maximum(destination.Layer);
        int distance;
        if (mission != null && mission.RangeSource == source && mission.RangeDestination == destination
            && mission.RangeMaximum == maximum) distance = mission.RangeDistance;
        else
        {
            distance = Find.WorldGrid.TraversalDistanceBetween(source, destination, true, maximum, true);
            if (mission != null)
            {
                mission.RangeSource = source; mission.RangeDestination = destination;
                mission.RangeMaximum = maximum; mission.RangeDistance = distance;
            }
        }
        return distance <= maximum;
    }
}
