using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace ModularTurrets;

public class WorkGiver_ModularTurretAssembly : WorkGiver_Scanner
{
    protected virtual bool HaulingOnly => false;
    private static ThingDef[] turretDefs;
    private static ThingDef[] TurretDefs => turretDefs ??= DefDatabase<ThingDef>.AllDefsListForReading
        .Where(def => def.thingClass != null && typeof(Building_ModularTurret).IsAssignableFrom(def.thingClass)).ToArray();

    public override PathEndMode PathEndMode => PathEndMode.Touch;

    internal static bool AllowsFloorTravel(TurretOperation kind)
        => kind is TurretOperation.Install or TurretOperation.UpgradeModule
            or TurretOperation.Damage or TurretOperation.Armor or TurretOperation.Range;

    public override bool ShouldSkip(Pawn pawn, bool forced = false)
    {
        // Use the game's per-def index: an unrelated map needs no building census.
        if (pawn?.Map is not { } map) return true;
        foreach (var def in TurretDefs)
            if (map.listerThings.ThingsOfDef(def).Count > 0) return false;
        return true;
    }

    public override IEnumerable<Thing> PotentialWorkThingsGlobal(Pawn pawn)
    {
        if (pawn?.Map is not { } map) yield break;
        foreach (var def in TurretDefs)
            foreach (var thing in map.listerThings.ThingsOfDef(def))
                if (thing.Faction == Faction.OfPlayer) yield return thing;
    }

    public override bool HasJobOnThing(Pawn pawn, Thing t, bool forced = false)
        => JobOnThing(pawn, t, forced) != null;

    public override Job JobOnThing(Pawn pawn, Thing t, bool forced = false)
    {
        if (t is not Building_ModularTurret turret || turret.PendingOrder == null) return null;
        bool workReady = turret.CanWork;
        if (HaulingOnly ? workReady : !workReady) return null;
        if (turret.IsWrecked && turret.PendingOrder.Kind != TurretOperation.Restore)
        {
            JobFailReason.Is(L.T("MT_WreckAssemblyStopped"));
            return null;
        }
        if (turret.AssemblyBlocked)
        {
            JobFailReason.Is(L.T("MT_AssemblyReturnBlockedDesc"));
            return null;
        }
        TurretOrder order = turret.PendingOrder;
        if (t.IsForbidden(pawn) || t.IsBurning()
            || !(AllowsFloorTravel(order.Kind) ? pawn.Map == t.Map : Building_ModularTurret.SameFloor(pawn, t))
            || !pawn.CanReserve(t, 1, -1, null, forced)
            || !pawn.CanReach(t, PathEndMode.Touch, forced ? Danger.Deadly : Danger.Some))
            return null;

        if (workReady)
        {
            if (pawn.WorkTagIsDisabled(WorkTags.Constructing)) return null;
            int requiredSkill = RequiredSkill(turret, order);
            if ((pawn.skills?.GetSkill(SkillDefOf.Construction)?.Level ?? 0) < requiredSkill)
            {
                JobFailReason.Is(L.T("MT_ConstructionRequired", requiredSkill));
                return null;
            }
            Job work = JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("MT_AssemblyWork"), turret);
            work.count = order.Id;
            return work;
        }

        if (!turret.CanReceive || pawn.WorkTagIsDisabled(WorkTags.Hauling)) return null;
        Thing material = FindMaterial(pawn, turret, order, forced);
        if (material == null) return null;
        Job haul = JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("MT_AssemblyHaul"), material, turret);
        // Both job types carry the immutable order ID, not an item count. The haul
        // driver calculates its carry amount separately so queued jobs cannot
        // silently deliver to an order which replaced the original one.
        haul.count = order.Id;
        return haul;
    }

    private static int RequiredSkill(Building_ModularTurret turret, TurretOrder order)
    {
        if (order.Kind == TurretOperation.Reload) return 0;
        int skill = turret.def.constructionSkillPrerequisite;
        if (order.Kind == TurretOperation.UpgradeModule)
            skill = Math.Max(skill, 6 + order.TargetTier * 2);
        else if (order.Kind is TurretOperation.Damage or TurretOperation.Armor or TurretOperation.Range)
            skill = Math.Max(skill, 12);
        return skill;
    }

    internal static Thing FindMaterial(Pawn pawn, Building_ModularTurret turret, TurretOrder order, bool forced)
    {
        IEnumerable<ThingDef> defs = order.Costs.Select(cost => cost.thingDef);
        if (order.Kind == TurretOperation.Install && order.PartDef != null)
            defs = defs.Append(order.PartDef);

        Thing material = defs.Where(def => def != null && turret.Needed(def) > 0).Distinct()
            .SelectMany(def => pawn.Map.listerThings.ThingsOfDef(def))
            .Where(thing => thing.Spawned && !thing.IsForbidden(pawn) && !thing.IsBurning()
                && (AllowsFloorTravel(order.Kind) ? thing.Map == pawn.Map : Building_ModularTurret.SameFloor(pawn, thing))
                && turret.Wants(thing)
                && pawn.carryTracker.MaxStackSpaceEver(thing.def) > 0
                && pawn.CanReserve(thing, 1, 1, null, forced))
            .OrderBy(thing => thing.Position.DistanceToSquared(pawn.Position))
            .FirstOrDefault(thing => pawn.CanReach(thing, PathEndMode.Touch,
                forced ? Danger.Deadly : Danger.Some));
        return material ?? (forced ? ReloadInTransit(pawn, turret, order) : null);
    }

    internal static Thing ReloadInTransit(Pawn pawn, Building_ModularTurret turret, TurretOrder order)
    {
        if (order.Kind != TurretOperation.Reload) return null;
        foreach (var reservation in pawn.Map.reservationManager.ReservationsReadOnly)
        {
            Pawn carrier = reservation.Claimant;
            Job current = carrier?.CurJob;
            if (reservation.Target.Thing != turret || reservation.Job != current || carrier == pawn
                || !carrier.Spawned || carrier.Faction != pawn.Faction || !Building_ModularTurret.SameFloor(pawn, carrier)
                || current?.def.driverClass != typeof(JobDriver_ModularTurretHaul)
                || current.targetB.Thing != turret || current.count != order.Id) continue;
            Thing carried = carrier.carryTracker.CarriedThing;
            if (carried != null && current.targetA.Thing == carried && turret.Wants(carried)
                && !carried.IsForbidden(pawn) && !carried.IsBurning()
                && pawn.carryTracker.MaxStackSpaceEver(carried.def) > 0
                && pawn.CanReach(carrier, PathEndMode.Touch, Danger.Deadly)) return carried;
        }
        return null;
    }
}
