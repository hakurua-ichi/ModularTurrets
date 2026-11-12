using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CombatExtended;
using CombatExtended.Lasers;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace ModularTurrets.Compat.CombatExtended;

public sealed class MountedMissileTrajectoryCE : LerpedTrajectoryWorker
{
    public override Vector3 MoveForward(ProjectileCE projectile)=>((Projectile_MountedMissileCE)projectile).PositionAt(projectile.FlightTicks);
    public override IEnumerable<Vector3> PredictPositions(ProjectileCE projectile,int ticks)
    {
        var missile=(Projectile_MountedMissileCE)projectile;
        for(int i=0;i<Math.Min(ticks,projectile.ticksToImpact);i++)yield return missile.PositionAt(projectile.FlightTicks+i);
    }
}
