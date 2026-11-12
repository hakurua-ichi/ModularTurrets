using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace ModularTurrets;

public sealed class ModuleEffectContext
{
    public Building_ModularTurret Turret;
    public Thing Part;
    public ModuleEffectDef Definition;
    public int Tier => Math.Clamp(Part.TryGetComp<CompTurretPart>()?.Tier ?? 0, 0, 5);
    public float Power => ParagonUtility.Power(Part);
}
