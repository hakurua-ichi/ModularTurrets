using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace ModularTurrets.Batteries;

public sealed class JobDriver_ModularBatteryHaul : JobDriver
{
    private int haulCount;
    private Building_ModularBattery Battery => job.GetTarget(TargetIndex.B).Thing as Building_ModularBattery;

    public override void ExposeData()
    {
        base.ExposeData();
        Scribe_Values.Look(ref haulCount, "modularBatteryHaulCount", 0);
    }

    // Installation and upgrade hauls may use stairs; hand-off needs this floor.
    private bool OrderStillValid(bool atDestination = false)
        => Battery?.CanReceive == true && Battery.PendingOrder?.Id == job.count
            && (!atDestination && WorkGiver_ModularBatteryAssembly.AllowsFloorTravel(Battery.PendingOrder.Kind)
                ? pawn.Map == Battery.Map : Building_ModularTurret.SameFloor(pawn, Battery));

    public override bool TryMakePreToilReservations(bool errorOnFailed)
    {
        Thing source = job.GetTarget(TargetIndex.A).Thing;
        if (!OrderStillValid() || source == null || !Battery.Wants(source)
            || !(WorkGiver_ModularBatteryAssembly.AllowsFloorTravel(Battery.PendingOrder.Kind)
                ? pawn.Map == source.Map : Building_ModularTurret.SameFloor(pawn, source))) return false;
        haulCount = Math.Min(Battery.Needed(source.def), Math.Min(source.stackCount,
            pawn.carryTracker.MaxStackSpaceEver(source.def)));
        return haulCount > 0
            && pawn.Reserve(Battery, job, 1, -1, null, errorOnFailed)
            && pawn.Reserve(source, job, 1, haulCount, null, errorOnFailed);
    }

    protected override IEnumerable<Toil> MakeNewToils()
    {
        this.FailOnDespawnedNullOrForbidden(TargetIndex.B);
        this.FailOn(() => !OrderStillValid());
        yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch)
            .FailOnDespawnedNullOrForbidden(TargetIndex.A);

        Toil carry = ToilMaker.MakeToil("ModularBatteryPickUpMaterial");
        carry.initAction = () =>
        {
            Thing source = job.GetTarget(TargetIndex.A).Thing;
            if (!OrderStillValid() || source == null || !source.Spawned
                || source.IsForbidden(pawn) || source.IsBurning()
                || !pawn.CanReachImmediate(source, PathEndMode.Touch)
                || !Battery.Wants(source) || !Building_ModularTurret.SameFloor(pawn, source))
            {
                EndJobWith(JobCondition.Incompletable);
                return;
            }
            int count = Math.Min(haulCount, Math.Min(source.stackCount, Battery.Needed(source.def)));
            if (count <= 0 || pawn.carryTracker.TryStartCarry(source, count) <= 0)
            {
                EndJobWith(JobCondition.Incompletable);
                return;
            }
            if (!Battery.BindCarriedInstallPart(source, pawn.carryTracker.CarriedThing, job.count))
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
        Toil deliver = ToilMaker.MakeToil("ModularBatteryDeliverMaterial");
        deliver.initAction = () =>
        {
            // Planning may cross floors; transfer still requires real local contact.
            if (!OrderStillValid(atDestination: true) || !pawn.CanReachImmediate(Battery, PathEndMode.Touch)
                || Battery.Receive(pawn.carryTracker.CarriedThing, job.count) <= 0)
                EndJobWith(JobCondition.Incompletable);
        };
        deliver.defaultCompleteMode = ToilCompleteMode.Instant;
        yield return deliver;
    }
}
