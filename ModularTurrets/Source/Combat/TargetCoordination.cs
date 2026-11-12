using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace ModularTurrets;

public sealed partial class Building_ModularTurret
{
    private TargetCoordinationBinding targetCoordination;
    internal TargetCoordinationBinding TargetCoordination => targetCoordination;
    internal bool CoordinationOwnerReady => Powered && !HoldFire && !IsStunned && !IsWrecked && CombatCompatibility.IsAvailable;
    internal bool UncoordinatedTarget(FireChannel channel, LocalTargetInfo target)
        => !target.HasThing || channel.Slot == "head" && (target == forcedTarget || OffMapMortar.HasMission(this));

    public void SetTargetCoordination(TargetCoordinationScope scope)
    {
        TargetCoordinationScope.RequireMainThread();
        if (targetCoordination?.Scope == scope) return;
        if (scope != null && !scope.Accepts(this)) throw new ArgumentException("Target coordination scope must match the turret's map and floor.");
        if (targetCoordination != null) DetachTargetCoordination(targetCoordination);
        if (scope != null)
        {
            targetCoordination = scope.Attach(this);
            ResetCoordinatedAutomaticTargets();
        }
    }
    internal void DetachTargetCoordination(TargetCoordinationBinding binding)
    {
        binding.Scope.Detach(binding);
        if (targetCoordination == binding) targetCoordination = null;
    }
    internal void ResetCoordinatedAutomaticTargets()
    {
        foreach (var channel in channels)
            if (channel.Target.HasThing && !UncoordinatedTarget(channel, channel.Target))
            {
                channel.Verb?.Reset(); channel.Target = LocalTargetInfo.Invalid;
                channel.Warmup = 0; channel.QuickRetarget = false;
            }
    }
}
