using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace ModularTurrets;

public sealed class TransportersArrivalAction_ModularMortar : TransportersArrivalAction
{
    private MapParent destination;
    private IntVec3 usedCell, intendedCell;
    public override bool GeneratesMap => false;
    public TransportersArrivalAction_ModularMortar() { }
    public TransportersArrivalAction_ModularMortar(MapParent destination, IntVec3 usedCell, IntVec3 intendedCell)
    { this.destination = destination; this.usedCell = usedCell; this.intendedCell = intendedCell; }
    public override void ExposeData()
    {
        base.ExposeData();
        Scribe_References.Look(ref destination, "destination");
        Scribe_Values.Look(ref usedCell, "usedCell");
        Scribe_Values.Look(ref intendedCell, "intendedCell");
        if (Scribe.mode == LoadSaveMode.PostLoadInit)
        {
            // Older flights saved the ordinary Core container default (false).
            // Keep their class/IDs and repair only this action's owning flight.
            foreach (var worldObject in Find.WorldObjects.AllWorldObjects)
            {
                if (worldObject is not TravellingTransporters flight || flight.def.defName != OffMapMortar.FlightDefName
                    || !ReferenceEquals(flight.arrivalAction, this)) continue;
                var holders = new List<IThingHolder>();
                flight.GetChildHolders(holders);
                foreach (var holder in holders)
                    if (holder is ActiveTransporterInfo payload) payload.innerContainer.dontTickContents = true;
                break;
            }
        }
    }
    // Always own this delivery, even when the target disappeared. Core must
    // never turn an undeliverable shell into a drop pod, gift, or caravan.
    public override FloatMenuAcceptanceReport StillValid(IEnumerable<IThingHolder> pods, PlanetTile tile) => true;
    public override void Arrived(List<ActiveTransporterInfo> transporters, PlanetTile tile)
    {
        Map map = destination?.Spawned == true && destination.Tile == tile ? destination.Map : null;
        if (map == null)
        {
            foreach (var payload in transporters) payload.innerContainer.ClearAndDestroyContents();
            Messages.Message(L.T("MT_OffMapTargetGone"), MessageTypeDefOf.NegativeEvent, false);
            return;
        }
        if (!OffMapGroundScope.TryEnter(map, out var ground))
        {
            foreach (var payload in transporters) payload.innerContainer.ClearAndDestroyContents();
            Messages.Message(L.T("MT_OffMapFloorFailed"), MessageTypeDefOf.NegativeEvent, false);
            return;
        }
        using (ground)
        foreach (var payload in transporters)
            while (payload.innerContainer.Count > 0)
            {
                Thing thing = payload.innerContainer[0]; payload.innerContainer.Remove(thing);
                if (thing is not Projectile_ModularExplosive projectile) { thing.Destroy(); continue; }
                if (!usedCell.InBounds(map)) { projectile.Destroy(); continue; }
                if (!OffMapGroundScope.SetGround(projectile))
                {
                    projectile.Destroy();
                    Messages.Message(L.T("MT_OffMapFloorFailed"), MessageTypeDefOf.NegativeEvent, false);
                    continue;
                }
                IntVec3 entry = new IntVec3(0, 0, Mathf.Clamp(usedCell.z, 0, map.Size.z - 1));
                GenSpawn.Spawn(projectile, entry, map);
                projectile.ResumeOffMap(entry.ToVector3Shifted(), usedCell, intendedCell);
            }
    }
}
