using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI;

namespace ModularTurrets;

public sealed partial class Building_ModularTurret
{
    // Vanilla GenPlace.Near searches out to 12.9 cells. Counting the same area
    // before each click makes a retry after partial placement safe.
    private const float DebugMaterialDropRadius = 12.9f;

    internal void DebugDropWreckRepairMaterials()
    {
        if (!Prefs.DevMode || !Spawned || Destroyed || !IsWrecked) return;

        Map map = Map;
        TurretOrder repair = PendingOrder?.Kind == TurretOperation.Restore ? PendingOrder : null;
        var costs = repair?.Costs ?? GetRestorationCosts();
        var missing = costs.Where(c => c?.thingDef != null && c.count > 0)
            .GroupBy(c => c.thingDef).ToDictionary(group => group.Key,
                group => repair != null ? Needed(group.Key) : group.First().count);
        var placementCells = new Dictionary<IntVec3, bool>();
        var footprint = this.OccupiedRect();

        bool Accessible(IntVec3 cell)
        {
            if (placementCells.TryGetValue(cell, out bool usable)) return usable;
            usable = cell.InBounds(map) && !footprint.Contains(cell) && cell.Standable(map) && !cell.Fogged(map)
                && map.reachability.CanReach(cell, this, PathEndMode.Touch, TraverseMode.PassDoors, Danger.Deadly);
            placementCells[cell] = usable;
            return usable;
        }

        bool Haulable(Thing item) => item.Spawned && SameFloor(this, item)
            && !item.IsForbidden(Faction.OfPlayer) && !item.IsBurning();

        void CountAvailable(Thing item)
        {
            if (item != null && missing.TryGetValue(item.def, out int needed))
                missing[item.def] = Math.Max(0, needed - item.stackCount);
        }

        if (missing.Values.Any(count => count > 0))
        {
            foreach (IntVec3 cell in GenRadial.RadialCellsAround(Position, DebugMaterialDropRadius, false))
            {
                if (!cell.InBounds(map)) continue;
                foreach (Thing item in cell.GetThingList(map))
                    if (missing.TryGetValue(item.def, out int needed) && needed > 0 && Haulable(item) && Accessible(cell))
                        CountAvailable(item);
            }
            if (repair != null)
                foreach (Pawn pawn in map.mapPawns.AllPawnsSpawned)
                {
                    var job = pawn.CurJob;
                    if (job?.def.defName == "MT_AssemblyHaul" && job.count == repair.Id
                        && job.targetB.Thing == this && SameFloor(pawn, this)) CountAvailable(pawn.carryTracker?.CarriedThing);
                }
        }

        long requested = missing.Values.Sum(count => (long)count);
        if (requested == 0)
        {
            Messages.Message(L.T("MT_DebugWreckMaterialsNone"), this, MessageTypeDefOf.NeutralEvent, false);
            return;
        }

        long placed = 0;
        foreach (var resource in missing)
        {
            int remaining = resource.Value;
            while (remaining > 0)
            {
                var stack = ThingMaker.MakeThing(resource.Key, resource.Key.MadeFromStuff ? GenStuff.DefaultStuffFor(resource.Key) : null);
                stack.stackCount = Math.Min(remaining, Math.Max(resource.Key.stackLimit, 1));
                int delivered = 0;
                bool complete = GenPlace.TryPlaceThing(stack, Position, map, ThingPlaceMode.Near, out _,
                    (_, count) => delivered += count,
                    cell => Accessible(cell) && !cell.GetThingList(map).Any(t => t.def == resource.Key && !Haulable(t)));
                placed += delivered;
                remaining -= delivered;
                if (!complete || delivered == 0)
                {
                    // Near can merge part of the stack before running out of room.
                    if (!stack.Spawned && !stack.Destroyed) stack.Destroy(DestroyMode.Vanish);
                    break;
                }
            }
        }
        string report = placed == requested ? L.T("MT_DebugWreckMaterialsDropped", placed)
            : L.T("MT_DebugWreckMaterialsPartial", placed, requested - placed);
        Messages.Message(report, this, placed == requested ? MessageTypeDefOf.NeutralEvent : MessageTypeDefOf.RejectInput, false);
    }
}
