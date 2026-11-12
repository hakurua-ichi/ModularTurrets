using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace ModularTurrets;

public sealed class JobDriver_ModularTurretWork : JobDriver
{
    private Building_ModularTurret Turret => job.GetTarget(TargetIndex.A).Thing as Building_ModularTurret;

    private bool OrderStillValid(bool atDestination = false)
        => Turret?.WorkReady(job.count) == true
            && (!atDestination && WorkGiver_ModularTurretAssembly.AllowsFloorTravel(Turret.PendingOrder.Kind)
                ? pawn.Map == Turret.Map : Building_ModularTurret.SameFloor(pawn, Turret));

    public override bool TryMakePreToilReservations(bool errorOnFailed)
        => OrderStillValid() && pawn.Reserve(Turret, job, 1, -1, null, errorOnFailed);

    protected override IEnumerable<Toil> MakeNewToils()
    {
        this.FailOnDespawnedNullOrForbidden(TargetIndex.A);
        this.FailOn(() => !OrderStillValid());
        yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);
        Toil work = ToilMaker.MakeToil("ModularTurretAssemblyWork");
        work.defaultCompleteMode = ToilCompleteMode.Never;
        work.tickAction = () =>
        {
            Building_ModularTurret turret = Turret;
            if (!OrderStillValid(atDestination: true) || !pawn.CanReachImmediate(turret, PathEndMode.Touch))
            {
                EndJobWith(JobCondition.Incompletable);
                return;
            }
            turret.DoWork(pawn.GetStatValue(StatDefOf.ConstructionSpeed), job.count);
            pawn.skills?.Learn(SkillDefOf.Construction, 0.1f);
            if (turret.PendingOrder?.Id != job.count) ReadyForNextToil();
            else if (pawn.IsHashIntervalTick(250)) pawn.jobs.CheckForJobOverride();
        };
        work.WithProgressBar(TargetIndex.A, () => Turret?.PendingOrder?.Id != job.count
            ? 1f : Mathf.Clamp01(1f - Turret.PendingOrder.WorkLeft / Math.Max(1f, Turret.PendingOrder.WorkTotal)));
        work.activeSkill = () => SkillDefOf.Construction;
        yield return work;
    }
}
