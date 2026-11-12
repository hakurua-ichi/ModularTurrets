using System;
using RimWorld;
using Verse;
using Verse.AI;

namespace ModularTurrets;

public sealed partial class Building_ModularTurret
{
    private ChannelSearcher GetTargetSearcher(FireChannel channel)
    {
        Verb verb = channel.Verb;
        if (channel.CachedSearcher is ChannelSearcher cached && cached.Matches(this, channel, verb)) return cached;
        var searcher = new ChannelSearcher(this, channel, verb);
        channel.CachedSearcher = searcher;
        return searcher;
    }

    // Runtime-only query context. Core still chooses targets and performs LOS;
    // no world/visibility result survives a search or a tick.
    private sealed class ChannelSearcher : IAttackTargetSearcher
    {
        private readonly Building_ModularTurret owner;
        private readonly FireChannel channel;
        internal readonly Predicate<Thing> Validator;

        internal ChannelSearcher(Building_ModularTurret owner, FireChannel channel, Verb verb)
        {
            this.owner = owner;
            this.channel = channel;
            CurrentEffectiveVerb = verb;
            Validator = ValidateCandidate;
        }

        internal bool Matches(Building_ModularTurret candidateOwner, FireChannel candidateChannel, Verb verb)
            => ReferenceEquals(owner, candidateOwner) && ReferenceEquals(channel, candidateChannel)
                && ReferenceEquals(CurrentEffectiveVerb, verb);

        private bool ValidateCandidate(Thing target)
            => SameFloor(owner, target) && target.HostileTo(owner)
                && (target is not Pawn pawn || !pawn.Downed && !pawn.IsPrisoner)
                && channel.Verb.CanHitTarget(target)
                && (owner.targetCoordination == null || owner.targetCoordination.CanReserve(channel, target));

        public Thing Thing => owner;
        public Verb CurrentEffectiveVerb { get; }
        public LocalTargetInfo LastAttackedTarget => owner.LastAttackedTarget;
        public int LastAttackTargetTick => owner.LastAttackTargetTick;
    }
}
