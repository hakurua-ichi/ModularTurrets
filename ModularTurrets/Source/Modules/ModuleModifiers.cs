using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace ModularTurrets;

public sealed class ModuleModifiers
{
    public float RangeFactor = 1f, RangeFlat, DamageFactor = 1f, ArmorBonus;
    public float AimingFactor = 1f, CoolingFactor = 1f, ShieldBonus, TurnSpeedFactor = 1f;
    public float AccuracyMissFactor = 1f;
    public float MortarScatterFactor = 1f;
    public bool PreventFriendlyFire;
}
