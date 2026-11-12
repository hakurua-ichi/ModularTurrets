using RimWorld;
using UnityEngine;
using Verse;

namespace ModularTurrets;

public sealed class HeadAccuracyExtension : DefModExtension
{
    public float missReductionPerTier = 0.1f;
    public bool guaranteedFlame;
    public float flameConeAngle = 90f;
}
