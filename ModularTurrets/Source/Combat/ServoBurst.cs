using System;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace ModularTurrets;

public sealed partial class Building_ModularTurret
{
    private void UpdateBurstHandoff(FireChannel channel, Verb_ModularBurst verb)
    {
        bool lostPawn = channel.Target.Thing is Pawn pawn && (pawn.Dead || pawn.Downed);
        if (!lostPawn && !verb.TurningForHandoff) return;
        bool hasServo = ActiveParagon("Servo") != null;
        if (lostPawn && hasServo)
        {
            var coordination = targetCoordination;
            var query = coordination == null ? default : coordination.BeginQuery(channel);
            Thing next = FindHandoffTarget(channel);
            if (next != null && (coordination != targetCoordination || coordination != null && !coordination.TryAcquire(channel, next, query))) next = null;
            if (next != null)
            {
                channel.Target = next;
                verb.RedirectBurst(next);
                OnAttackedTarget(next);
                lostPawn = false;
            }
        }
        if (lostPawn && !hasServo && !verb.TurningForHandoff) return;
        if (lostPawn || !hasServo || !ValidTarget(channel, channel.Target))
        {
            targetCoordination?.Release(channel);
            verb.Reset();
            channel.CastComplete?.Invoke();
            channel.Target = LocalTargetInfo.Invalid;
            channel.QuickRetarget = lostPawn && hasServo;
            return;
        }
        verb.AlignHandoff(IsFacing(channel.Target));
    }

    private bool IsFacing(LocalTargetInfo target)
        => RotationTo(target) <= 8f;

    private float RotationTo(LocalTargetInfo target)
        => Mathf.Abs(Mathf.DeltaAngle(aimAngle, (target.CenterVector3 - DrawPos).AngleFlat()));

    private Thing FindHandoffTarget(FireChannel channel)
    {
        var searcher = GetTargetSearcher(channel);
        Thing best = null;
        float bestAngle = float.PositiveInfinity;
        foreach (var entry in Map.attackTargetsCache.GetPotentialTargetsFor(searcher))
        {
            Thing target = entry.Thing;
            if (!target.Spawned || target.Map != Map || !target.HostileTo(this)) continue;
            float angle = RotationTo(target);
            if (angle > bestAngle || angle == bestAngle && best != null && target.thingIDNumber >= best.thingIDNumber) continue;
            if (entry.ThreatDisabled(searcher) || !AttackTargetFinder.IsAutoTargetable(entry)
                || !ValidTarget(channel, target) || PreventFriendlyFire && FriendlyFireSafety.IsProtectedPawn(target, Faction)) continue;
            if (Faction == Faction.OfPlayer && !HasVisibleCell(target)) continue;
            if (target is Pawn candidate && !candidate.IsCombatant() && !GenSight.LineOfSightToThing(Position, candidate, Map)) continue;
            if (channel.Verb.ProjectileFliesOverhead())
            {
                if (target.Position.GetRoof(Map)?.isThickRoof == true) continue;
            }
            else if (channel.Verb.verbProps.requireLineOfSight
                && (!ClearOfBlindSmoke(Position) || !ClearOfBlindSmoke(target.Position) || !this.CanSee(target, ClearOfBlindSmoke))) continue;
            if (targetCoordination != null && !targetCoordination.CanReserve(channel, target)) continue;
            best = target;
            bestAngle = angle;
        }
        return best;
    }

    private bool ClearOfBlindSmoke(IntVec3 cell) => !cell.AnyGas(Map, GasType.BlindSmoke);

    private bool HasVisibleCell(Thing target)
    {
        foreach (var cell in target.OccupiedRect())
            if (!cell.Fogged(Map)) return true;
        return false;
    }
}
