using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace ModularTurrets;

public class ModuleEffectWorker
{
    public virtual void ModifyStats(ModuleEffectContext context, ModuleModifiers modifiers) { }
    public virtual void TransformShots(ModuleEffectContext context, Thing equipment, List<ModuleShot> shots) { }
}
