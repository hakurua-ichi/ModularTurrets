using System;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace ModularTurrets;

// Keep Core's remaining shots, opening shot, and fire interval when only the target changes.
public abstract class Verb_ModularBurst : Verb_Shoot
{
    private bool turningForHandoff;
    internal bool TurningForHandoff => turningForHandoff;

    internal void RedirectBurst(LocalTargetInfo target)
    {
        currentTarget = target;
        turningForHandoff = true;
    }

    internal void AlignHandoff(bool aligned)
    {
        // A moving target can leave the firing arc again before the shot is due.
        turningForHandoff = !aligned || ticksToNextBurstShot > 1;
        // VerbTick decrements before firing. Let the interval elapse while turning,
        // but keep the next shot one tick away until the barrel is aligned.
        if (!aligned) ticksToNextBurstShot = Math.Max(2, ticksToNextBurstShot);
    }

    public override void Reset()
    {
        base.Reset();
        turningForHandoff = false;
    }

    public override void ExposeData()
    {
        base.ExposeData();
        Scribe_Values.Look(ref turningForHandoff, "turningForServoHandoff", false);
    }
}
