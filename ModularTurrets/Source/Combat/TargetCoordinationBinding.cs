using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace ModularTurrets;

internal sealed class TargetCoordinationBinding
{
    internal readonly Building_ModularTurret Owner;
    internal readonly TargetCoordinationScope Scope;
    internal int Generation;
    private readonly Dictionary<FireChannel, Held> held = new();
    private readonly record struct Held(TargetCoordinationScope.Lease Token, Thing Target, Thing Part, Thing Gun);
    internal readonly record struct Query(long Epoch, long Revision, int Generation,
        FireChannel Channel, string Slot, Thing Part, Thing Gun);
    internal TargetCoordinationBinding(Building_ModularTurret owner, TargetCoordinationScope scope)
    { Owner = owner; Scope = scope; }
    internal bool CanReserve(FireChannel channel, Thing target) => Scope.CanReserve(this, channel, target);
    internal Query BeginQuery(FireChannel channel)
        => new(Scope.Epoch, Scope.Revision, Generation, channel, channel.Slot, channel.Part, channel.Gun);
    private bool QueryCurrent(FireChannel channel, Query query)
    {
        if (Scope.Disposed || query.Epoch != Scope.Epoch || query.Revision != Scope.Revision
            || query.Generation != Generation || Owner.TargetCoordination != this
            || query.Channel != channel || query.Slot != channel.Slot
            || query.Part != channel.Part || query.Gun != channel.Gun
            || channel.Part?.Destroyed == true || channel.Gun?.Destroyed == true) return false;
        var current = Owner.CombatChannels;
        for (int i = 0; i < current.Count; i++) if (current[i] == channel) return true;
        return false;
    }
    internal bool TryAcquire(FireChannel channel, Thing target, Query query)
    {
        if (!QueryCurrent(channel, query) || !CanReserve(channel, target) || !QueryCurrent(channel, query)) return false;
        // All callback-bearing checks finished. A stale token cannot remove a new claim.
        Release(channel);
        held[channel] = new Held(Scope.Grant(this, channel, target), target, channel.Part, channel.Gun);
        return true;
    }
    internal bool IsLive(TargetCoordinationScope.Lease lease)
    {
        if (lease.Scope != Scope || lease.Epoch != Scope.Epoch || lease.Generation != Generation
            || !Scope.Accepts(Owner) || Owner.TargetCoordination != this || !Owner.CoordinationOwnerReady) return false;
        foreach (var entry in held)
            if (entry.Value.Token == lease)
            {
                Thing target = entry.Value.Target;
                return Matches(entry.Key, entry.Value) && !Owner.UncoordinatedTarget(entry.Key, entry.Key.Target)
                    && target.Spawned && !target.Destroyed && target.Map == Scope.Map
                    && Building_ModularTurret.SameFloor(Owner, target) && target.HostileTo(Owner)
                    && (target is not Pawn pawn || !pawn.Dead && !pawn.Downed && !pawn.IsPrisoner)
                    && Scope.IsCurrent(lease);
            }
        return false;
    }
    internal bool OwnsCurrent(FireChannel channel)
        // Target eligibility belongs to the existing native combat path. In
        // particular, Servo must see its dead target before Reset/retargeting.
        => held.TryGetValue(channel, out var entry) && Matches(channel, entry)
            && Scope.Accepts(Owner) && Owner.TargetCoordination == this && Scope.IsCurrent(entry.Token);
    private bool Matches(FireChannel channel, Held entry)
        => entry.Token.Scope == Scope && entry.Token.Epoch == Scope.Epoch && entry.Token.Generation == Generation
            && channel.Target.Thing == entry.Target && channel.Slot == entry.Token.Slot
            && channel.Part == entry.Part && channel.Gun == entry.Gun
            && entry.Part?.Destroyed != true && entry.Gun?.Destroyed != true;
    internal void Release(FireChannel channel)
    {
        if (held.Remove(channel, out var entry)) Scope.Release(entry.Token);
    }
    internal void ReleaseHead()
    {
        foreach (var channel in Owner.CombatChannels) if (channel.Slot == "head") Release(channel);
    }
    internal void ReleaseAll()
    {
        foreach (var entry in held.Values) Scope.Release(entry.Token);
        held.Clear();
    }
    internal void Reconfigure()
    {
        ReleaseAll(); Generation++; Owner.ResetCoordinatedAutomaticTargets();
    }
}
