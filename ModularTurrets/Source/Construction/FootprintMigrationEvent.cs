using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using RimWorld;
using UnityEngine;
using Verse;

namespace ModularTurrets;

public sealed class FootprintMigrationEvent : IExposable
{
    public string DefName, ThingId, Reason;
    public IntVec3 From, To;
    public void ExposeData()
    {
        Scribe_Values.Look(ref DefName, "defName"); Scribe_Values.Look(ref ThingId, "thingId");
        Scribe_Values.Look(ref Reason, "reason"); Scribe_Values.Look(ref From, "from"); Scribe_Values.Look(ref To, "to");
    }
}
