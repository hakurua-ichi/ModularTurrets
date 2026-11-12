using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace ModularTurrets;

internal static class SpecialistHits
{
    internal static void Pulse(Building_ModularTurret turret, Thing equipment, Thing hit, DamageWorker.DamageResult result)
    {
        var spec = turret.SpecialistFor(equipment);
        if (spec?.kind != "Pulse" || spec.stunRecoveryHediff == null || result.totalDamageDealt <= 0f
            || hit is not Pawn pawn || pawn.Dead || pawn.Downed || !pawn.Spawned || pawn.stances?.stunner == null
            || pawn.health.hediffSet.HasHediff(spec.stunRecoveryHediff)
            || turret.PreventFriendlyFire && FriendlyFireSafety.IsProtectedPawn(pawn, turret.Faction)) return;
        int duration = Math.Clamp(spec.stunTicks, 1, 600);
        var recovery = HediffMaker.MakeHediff(spec.stunRecoveryHediff, pawn);
        var expiry = recovery.TryGetComp<HediffComp_Disappears>();
        if (expiry == null) return; // Missing cooldown data must not permit a permanent stun loop.
        expiry.SetDuration(Math.Clamp(Math.Max(duration + 60, spec.stunRecoveryTicks), duration + 60, 3600));
        pawn.health.AddHediff(recovery);
        // Use Core's immunity/resistance path. StunFor alone bypasses those checks.
        // The saved Pawn hediff is shared across all turrets, not a per-turret timer.
        pawn.stances.stunner.Notify_DamageApplied(new DamageInfo(DamageDefOf.Stun, duration / 30f, instigator: turret));
    }

    internal static void Panic(Building_ModularTurret turret, Thing equipment, Thing hit, DamageWorker.DamageResult result)
    {
        var spec = turret.SpecialistFor(equipment);
        if (spec?.kind != "Panic" || result.totalDamageDealt <= 0f || hit is not Pawn pawn || pawn.Dead || pawn.Downed
            || !pawn.Spawned || !pawn.RaceProps.IsFlesh || pawn.InMentalState || pawn.jobs == null || pawn.CurJobDef == JobDefOf.Flee
            || turret.PreventFriendlyFire && FriendlyFireSafety.IsProtectedPawn(pawn, turret.Faction)
            || !Rand.Chance(Mathf.Clamp01(spec.panicChance))) return;
        // A bounded ordinary flee job, not a persistent mental break or faction change.
        IntVec3 dest = CellFinderLoose.GetFleeDest(pawn, new List<Thing> { turret }, 12f);
        if (!dest.IsValid || dest == pawn.Position || !dest.InBounds(pawn.Map)) return;
        Job flee = JobMaker.MakeJob(JobDefOf.Flee, dest, turret);
        flee.expiryInterval = Math.Clamp(spec.panicTicks, 1, 3600);
        flee.checkOverrideOnExpire = false;
        flee.exitMapOnArrival = false;
        pawn.jobs.StartJob(flee, JobCondition.InterruptForced, resumeCurJobAfterwards: false);
    }

    internal static void Disrupt(Building_ModularTurret turret, Thing equipment, Thing hit, DamageInfo primary, DamageWorker.DamageResult result)
    {
        var spec = turret.SpecialistFor(equipment);
        if (spec?.kind != "Disruptor" || result.totalDamageDealt <= 0f || hit is not Pawn pawn || pawn.Dead || pawn.Destroyed
            || turret.PreventFriendlyFire && FriendlyFireSafety.IsProtectedPawn(pawn, turret.Faction)) return;
        // Enhance the actually hit surviving part. Never directly remove a body part.
        BodyPartRecord part = result.LastHitPart;
        if (part != null && pawn.health.hediffSet.GetNotMissingParts().Contains(part)
            && spec.limbDamageFactor > 1f && Rand.Chance(Mathf.Clamp01(spec.limbDamageChance)))
        {
            var extra = new DamageInfo(primary);
            extra.SetHitPart(part);
            extra.SetAmount(primary.Amount * Math.Max(0f, SpecialistModuleExtension.Factor(spec.limbDamageFactor, 1f) - 1f));
            pawn.TakeDamage(extra);
        }
        if (pawn.Dead || pawn.Destroyed || spec.internalDamageFactor <= 0f) return;
        var remaining = pawn.health.hediffSet.GetNotMissingParts().ToList();
        var internals = remaining.Where(p => p.depth == BodyPartDepth.Inside).ToList();
        BodyPartRecord secondary = internals.Count > 0 ? internals.RandomElement()
            : remaining.Contains(pawn.RaceProps.body.corePart) ? pawn.RaceProps.body.corePart : null;
        if (secondary == null) return;
        var internalHit = new DamageInfo(primary);
        internalHit.SetHitPart(secondary);
        internalHit.SetAmount(primary.Amount * Mathf.Clamp01(spec.internalDamageFactor));
        // Retains armor, shields, normal anatomy and instant-kill protection.
        pawn.TakeDamage(internalHit);
    }
}
