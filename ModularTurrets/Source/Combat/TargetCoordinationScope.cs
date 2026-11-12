using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace ModularTurrets;

// Explicit consumer ownership only: no map component, tick, static registry or save data.
public sealed class TargetCoordinationScope : IDisposable
{
    internal readonly Map Map;
    internal readonly int Floor;
    internal long Epoch = 1, Revision;
    internal bool Disposed;
    private long serial;
    private readonly Dictionary<int, Lease> claims = new();
    private readonly Dictionary<int, TargetCoordinationBinding> owners = new();
    internal readonly record struct Lease(TargetCoordinationScope Scope, long Epoch, long Serial,
        int Owner, string Slot, int Generation, int Target);

    public TargetCoordinationScope(Map map, int floor)
    {
        // Claims follow live channel ownership. Callers opt in on the game thread;
        // this scope never moves a Thing or substitutes its damage/LOS checks.
        RequireMainThread();
        Map = map ?? throw new ArgumentNullException(nameof(map)); Floor = floor;
    }
    internal static void RequireMainThread()
    {
        if (!UnityData.IsInMainThread) throw new InvalidOperationException("Target coordination is main-thread only.");
    }
    internal bool Accepts(Building_ModularTurret owner)
        => !Disposed && owner.Spawned && owner.Map == Map
            && FloorCompatibility.TryGetFloor(owner, out int floor) && floor == Floor;
    internal TargetCoordinationBinding Attach(Building_ModularTurret owner)
    {
        if (!Accepts(owner)) throw new ArgumentException("Target coordination scope must match the turret's map and floor.");
        var binding = new TargetCoordinationBinding(owner, this);
        owners.Add(owner.thingIDNumber, binding); return binding;
    }
    internal bool CanReserve(TargetCoordinationBinding binding, FireChannel channel, Thing target)
    {
        if (!Accepts(binding.Owner) || !binding.Owner.CoordinationOwnerReady
            || binding.Owner.TargetCoordination != binding || target == null || !target.Spawned
            || target.Destroyed || target.Map != Map || !Building_ModularTurret.SameFloor(binding.Owner, target)) return false;
        if (!claims.TryGetValue(target.thingIDNumber, out var claim)) return true;
        if (!owners.TryGetValue(claim.Owner, out var incumbent) || !incumbent.IsLive(claim)) return true;
        return incumbent == binding && claim.Slot == channel.Slot && claim.Generation == binding.Generation;
    }
    internal Lease Grant(TargetCoordinationBinding binding, FireChannel channel, Thing target)
    {
        var lease = new Lease(this, Epoch, ++serial, binding.Owner.thingIDNumber, channel.Slot,
            binding.Generation, target.thingIDNumber);
        claims[target.thingIDNumber] = lease; Revision++; return lease;
    }
    internal void Release(Lease lease)
    {
        if (claims.TryGetValue(lease.Target, out var current) && current == lease)
        { claims.Remove(lease.Target); Revision++; }
    }
    internal bool IsCurrent(Lease lease)
        => claims.TryGetValue(lease.Target, out var current) && current == lease;
    internal void Detach(TargetCoordinationBinding binding)
    {
        binding.ReleaseAll();
        if (owners.TryGetValue(binding.Owner.thingIDNumber, out var current) && current == binding)
            owners.Remove(binding.Owner.thingIDNumber);
    }
    public void Clear()
    {
        RequireMainThread();
        if (Disposed) throw new ObjectDisposedException(nameof(TargetCoordinationScope));
        Epoch++; Revision++; claims.Clear();
    }
    public void Dispose()
    {
        RequireMainThread();
        if (Disposed) return;
        Disposed = true; Epoch++; Revision++; claims.Clear();
        var snapshot = owners.Values.ToArray(); owners.Clear();
        foreach (var binding in snapshot) binding.Owner.DetachTargetCoordination(binding);
    }
}
