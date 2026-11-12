using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using RimWorld;
using UnityEngine;
using Verse;

namespace ModularTurrets;

public sealed class InstalledMount : IExposable
{
    public string Slot;
    public Thing Part;
    public void ExposeData() { Scribe_Values.Look(ref Slot, "slot"); Scribe_References.Look(ref Part, "part"); }
}
