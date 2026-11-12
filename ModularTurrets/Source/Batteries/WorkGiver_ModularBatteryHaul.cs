using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace ModularTurrets.Batteries;

public sealed class WorkGiver_ModularBatteryHaul : WorkGiver_ModularBatteryAssembly
{
    protected override bool HaulingOnly => true;
}
