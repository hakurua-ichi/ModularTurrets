using System;
using System.Linq;
using CombatExtended;
using RimWorld;
using UnityEngine;
using Verse;

namespace ModularTurrets.Compat.CombatExtended;

internal interface IMountedAiming
{
    bool TickMountedAim();
}
