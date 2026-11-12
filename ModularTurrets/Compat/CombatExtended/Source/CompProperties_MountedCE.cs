using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using CombatExtended;
using CombatExtended.Compatibility;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace ModularTurrets.Compat.CombatExtended;

public sealed class CompProperties_MountedCE : CompProperties
{
    public CompProperties_MountedCE()=>compClass=typeof(CompMountedCE);
}
