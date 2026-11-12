using System;
using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace ModularTurrets;

public sealed class OffMapMortarMission : IExposable
{
    public MapParent Destination;
    public IntVec3 Cell;
    public int SourceMapId;
    internal PlanetTile RangeSource = PlanetTile.Invalid, RangeDestination = PlanetTile.Invalid;
    internal int RangeMaximum = -1, RangeDistance;
    public void ExposeData()
    {
        Scribe_References.Look(ref Destination, "destination");
        Scribe_Values.Look(ref Cell, "cell");
        Scribe_Values.Look(ref SourceMapId, "sourceMapId");
    }
}
