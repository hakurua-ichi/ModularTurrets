using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace ModularTurrets;

public sealed class FireChannel : IExposable
{
    public string Slot;
    public Thing Gun;
    public Thing Part;
    public int Cooldown, Warmup;
    public bool OpeningShotReady, QuickRetarget;
    // An optional combat API may pause a burst while its normal reload runs.
    // Recomputed each tick; never persisted across adapter changes.
    public bool CompatibilityBlocked;
    internal Action CastComplete;
    internal IAttackTargetSearcher CachedSearcher;
    public LocalTargetInfo Target = LocalTargetInfo.Invalid;
    public Verb Verb => Gun?.TryGetComp<CompEquippable>()?.PrimaryVerb;
    public void ExposeData()
    {
        Scribe_Values.Look(ref Slot, "slot"); Scribe_Deep.Look(ref Gun, "gun"); Scribe_References.Look(ref Part, "part");
        Scribe_Values.Look(ref Cooldown, "cooldown"); Scribe_Values.Look(ref Warmup, "warmup"); Scribe_TargetInfo.Look(ref Target, "target");
        Scribe_Values.Look(ref OpeningShotReady, "paragonOpeningShotReady", false);
        Scribe_Values.Look(ref QuickRetarget, "paragonQuickRetarget", false);
    }
}
