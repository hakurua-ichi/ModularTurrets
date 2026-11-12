using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace ModularTurrets;

public sealed class WorkGiver_ModularTurretHaul : WorkGiver_ModularTurretAssembly
{
    protected override bool HaulingOnly => true;
}
