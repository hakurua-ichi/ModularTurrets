using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace ModularTurrets;

public sealed class ModuleShot
{
    public LocalTargetInfo Target;
    public float DamageFactor = 1f;
    public bool IsBranch;
}
