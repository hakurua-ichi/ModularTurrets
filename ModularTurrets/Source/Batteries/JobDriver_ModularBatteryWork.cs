using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace ModularTurrets.Batteries;

public sealed class JobDriver_ModularBatteryWork : JobDriver
{
    private Building_ModularBattery Battery => job.GetTarget(TargetIndex.A).Thing as Building_ModularBattery;

    private bool OrderStillValid()
        => Battery?.WorkReady(job.count) == true
            && (WorkGiver_ModularBatteryAssembly.AllowsFloorTravel(Battery.PendingOrder.Kind)
                ? pawn.Map == Battery.Map : Building_ModularTurret.SameFloor(pawn, Battery));

    public override bool TryMakePreToilReservations(bool errorOnFailed)
        => OrderStillValid() && pawn.Reserve(Battery, job, 1, -1, null, errorOnFailed);

    protected override IEnumerable<Toil> MakeNewToils()
    {
        this.FailOnDespawnedNullOrForbidden(TargetIndex.A);
        yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch).FailOn(() => !OrderStillValid());
        Toil work = ToilMaker.MakeToil("ModularBatteryAssemblyWork");
        work.defaultCompleteMode = ToilCompleteMode.Never;
        work.tickAction = () =>
        {
            Building_ModularBattery battery = Battery;
            if (!Building_ModularTurret.SameFloor(pawn, battery) || !pawn.CanReachImmediate(battery, PathEndMode.Touch)
                || !battery.TryDoWork(pawn.GetStatValue(StatDefOf.ConstructionSpeed), job.count))
            {
                EndJobWith(JobCondition.Incompletable);
                return;
            }
            pawn.skills?.Learn(SkillDefOf.Construction, 0.1f);
            if (battery.PendingOrder?.Id != job.count) ReadyForNextToil();
            else if (pawn.IsHashIntervalTick(250)) pawn.jobs.CheckForJobOverride();
        };
        work.WithProgressBar(TargetIndex.A, () => Battery?.PendingOrder?.Id != job.count
            ? 1f : Mathf.Clamp01(1f - Battery.PendingOrder.WorkLeft / Math.Max(1f, Battery.PendingOrder.WorkTotal)));
        work.activeSkill = () => SkillDefOf.Construction;
        yield return work;
    }
}
