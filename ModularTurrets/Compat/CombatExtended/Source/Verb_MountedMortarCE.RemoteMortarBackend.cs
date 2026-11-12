using System;
using System.Collections.Generic;
using global::CombatExtended;
using global::CombatExtended.WorldObjects;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace ModularTurrets.Compat.CombatExtended;

public partial class Verb_MountedMortarCE
{
    private PlanetTile checkedSource, checkedDestination;
    private int checkedMaximum = -1;
    private bool withinMaximum;

    internal bool WithinShuttleLimit(PlanetTile source, PlanetTile destination, float nativeRange)
    {
        int maximum = OffMapMortarRange.Maximum(destination.Layer);
        float factor = destination.LayerDef.rangeDistanceFactor;
        if (!float.IsFinite(factor) || factor <= 0f) return false;
        // CE compares integer, layer-weighted travel distances. Its ten-tile stock
        // mortar already excludes the first tile beyond the 62-tile surface cap.
        if (nativeRange < Math.Floor((maximum + 1d) * factor)) return true;
        if (checkedSource != source || checkedDestination != destination || checkedMaximum != maximum)
        {
            checkedSource = source; checkedDestination = destination; checkedMaximum = maximum;
            withinMaximum = OffMapMortarRange.Within(source, destination);
        }
        return withinMaximum;
    }

    public int WorldSourceMapId = -1;
    public void PrepareWorldTarget(Building_ModularTurret owner, GlobalTargetInfo target)
    {
        globalSourceInfo = new GlobalTargetInfo(owner.Position, owner.Map);
        globalTargetInfo = target;
        mokeTargetInfo = GetLocalTargetFor(target);
        WorldSourceMapId = owner.Map.uniqueID;
    }
    public void ClearWorldTarget()
    {
        globalTargetInfo = globalSourceInfo = GlobalTargetInfo.Invalid;
        mokeTargetInfo = LocalTargetInfo.Invalid; WorldSourceMapId = -1;
    }
    internal bool WorldReadyToCast()
    {
        if (caster is not Building_ModularTurret owner) return false;
        if (WorldSourceMapId != owner.Map?.uniqueID || !RemoteMortarBackend.Validate(owner, this, globalTargetInfo, false, out _))
        { RemoteMortarBackend.ClearWorldMission(owner); return false; }
        return owner.Powered && !owner.HoldFire && RemoteMortarBackend.SourceOpen(owner);
    }
    public override ShiftVecReport ShiftVecReportFor(GlobalTargetInfo target)
    {
        if (!target.IsMapTarget || target.Map == null) return base.ShiftVecReportFor(target);
        if (!FloorCompatibility.TryEnter(target.Map, 0, out var scope))
        {
            if (caster is Building_ModularTurret owner) RemoteMortarBackend.ClearWorldMission(owner);
            throw new InvalidOperationException("Mounted CE mortar cannot establish the target ground-floor scope.");
        }
        using (scope) return base.ShiftVecReportFor(target);
    }
    public override void ExposeData()
    {
        base.ExposeData(); ExposeMountedAim(); Scribe_Values.Look(ref WorldSourceMapId, "mtCEWorldSourceMapId", -1);
    }
    public override bool CanHitTargetFrom(IntVec3 root, LocalTargetInfo target, out string report)
    {
        if (globalTargetInfo.IsValid && caster is Building_ModularTurret owner && !target.HasThing && target.Cell == mokeTargetInfo.Cell)
        {
            if (WorldSourceMapId != owner.Map?.uniqueID) { report = "CE_ArtilleryTarget_InvalidTarget".Translate(); return false; }
            if (!RemoteMortarBackend.Validate(owner, this, globalTargetInfo, false, out report)) return false;
            if (!RemoteMortarBackend.SourceOpen(owner)) { report = "CE_BlockedRoof".Translate(); return false; }
            report = "";
            // CE's Building_TurretGunCE does the same local-range bypass only
            // after a world mission passed its separate CE shelling range.
            return true;
        }
        return base.CanHitTargetFrom(root, target, out report);
    }
}
