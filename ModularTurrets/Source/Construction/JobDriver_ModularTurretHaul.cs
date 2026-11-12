using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace ModularTurrets;

public sealed class JobDriver_ModularTurretHaul : JobDriver
{
    private int haulCount;
    private Building_ModularTurret Turret => job.GetTarget(TargetIndex.B).Thing as Building_ModularTurret;

    public override void ExposeData()
    {
        base.ExposeData();
        Scribe_Values.Look(ref haulCount, "modularHaulCount", 0);
    }

    // Installation and upgrade hauls may use stairs; hand-off needs this floor.
    private bool OrderStillValid(bool atDestination = false)
        => Turret?.CanReceive == true && Turret.PendingOrder?.Id == job.count
            && (!atDestination && WorkGiver_ModularTurretAssembly.AllowsFloorTravel(Turret.PendingOrder.Kind)
                ? pawn.Map == Turret.Map : Building_ModularTurret.SameFloor(pawn, Turret));

    public override bool TryMakePreToilReservations(bool errorOnFailed)
    {
        Thing source = job.GetTarget(TargetIndex.A).Thing;
        if (!OrderStillValid() || source == null || !Turret.Wants(source)) return false;
        bool inTransit = job.playerForced && !source.Spawned
            && WorkGiver_ModularTurretAssembly.ReloadInTransit(pawn, Turret, Turret.PendingOrder) == source;
        if (!inTransit && !(WorkGiver_ModularTurretAssembly.AllowsFloorTravel(Turret.PendingOrder.Kind)
            ? pawn.Map == source.Map : Building_ModularTurret.SameFloor(pawn, source))) return false;
        if (inTransit)
        {
            if (!pawn.Reserve(Turret, job, 1, -1, null, errorOnFailed)) return false;
            if (!OrderStillValid()) return false;
            // Core interrupts the previous reservation's job and returns its carry.
            // Dropping can merge stacks, so resolve the real spawned item again.
            source = WorkGiver_ModularTurretAssembly.FindMaterial(pawn, Turret, Turret.PendingOrder, false);
            if (source == null) return false;
            job.SetTarget(TargetIndex.A, source);
        }
        haulCount = Math.Min(Turret.Needed(source.def), Math.Min(source.stackCount,
            pawn.carryTracker.MaxStackSpaceEver(source.def)));
        return haulCount > 0
            && pawn.Reserve(Turret, job, 1, -1, null, errorOnFailed)
            && pawn.Reserve(source, job, 1, haulCount, null, errorOnFailed);
    }

    protected override IEnumerable<Toil> MakeNewToils()
    {
        this.FailOnDespawnedNullOrForbidden(TargetIndex.B);
        this.FailOn(() => !OrderStillValid());
        yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch)
            .FailOnDespawnedNullOrForbidden(TargetIndex.A);

        Toil carry = ToilMaker.MakeToil("ModularTurretPickUpMaterial");
        carry.initAction = () =>
        {
            Thing source = job.GetTarget(TargetIndex.A).Thing;
            if (!OrderStillValid() || source == null || !source.Spawned
                || source.IsForbidden(pawn) || source.IsBurning()
                || !pawn.CanReachImmediate(source, PathEndMode.Touch)
                || !Turret.Wants(source) || !Building_ModularTurret.SameFloor(pawn, source))
            {
                EndJobWith(JobCondition.Incompletable);
                return;
            }
            int count = Math.Min(haulCount, Math.Min(source.stackCount, Turret.Needed(source.def)));
            if (count <= 0 || pawn.carryTracker.TryStartCarry(source, count) <= 0)
            {
                EndJobWith(JobCondition.Incompletable);
                return;
            }
            job.SetTarget(TargetIndex.A, pawn.carryTracker.CarriedThing);
            pawn.records?.Increment(RecordDefOf.ThingsHauled);
        };
        carry.defaultCompleteMode = ToilCompleteMode.Instant;
        yield return carry;

        yield return Toils_Goto.GotoThing(TargetIndex.B, PathEndMode.Touch);
        Toil deliver = ToilMaker.MakeToil("ModularTurretDeliverMaterial");
        deliver.initAction = () =>
        {
            if (!OrderStillValid(atDestination: true) || !pawn.CanReachImmediate(Turret, PathEndMode.Touch)
                || Turret.Receive(pawn.carryTracker.CarriedThing, job.count) <= 0)
                EndJobWith(JobCondition.Incompletable);
        };
        deliver.defaultCompleteMode = ToilCompleteMode.Instant;
        yield return deliver;
    }
}
