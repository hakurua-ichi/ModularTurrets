using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace ModularTurrets;

// A fixed-function part; these values never scale with the ordinary part tier.
public sealed class SpecialistModuleExtension : DefModExtension
{
    public List<ThingDef> compatibleHeads = new();
    public List<string> allowedSlots = new();
    public string kind;
    public float explosionRadius = 1.4f;
    public float damageFactor = 1f, explosionRadiusFactor = 1f, autoReloadRadius = 6f;
    public int penetrationTargets = 3;
    public float penetrationDamageFactor = 0.7f;
    public float warmupFactor = 0.25f, cooldownFactor = 0.25f, burstCountFactor = 0.25f;
    public float minimumArmorPenetration = 1f;
    public float panicChance = 0.2f;
    public int panicTicks = 360;
    public float limbDamageChance = 0.2f, limbDamageFactor = 1.5f, internalDamageFactor = 0.25f;
    public int stunTicks = 120, stunRecoveryTicks = 300;
    public HediffDef stunRecoveryHediff;

    public override IEnumerable<string> ConfigErrors()
    {
        foreach (string error in base.ConfigErrors()) yield return error;
        if (compatibleHeads == null || compatibleHeads.Count == 0 || compatibleHeads.Any(h => h == null)
            || allowedSlots == null || allowedSlots.Count == 0 || allowedSlots.Any(s => s is not ("left" or "right" or "top")))
            yield return L.T("MT_SpecialistConfigMount");
        if (kind is not ("Explosive" or "Penetrator" or "Cycle" or "Quantum" or "Panic" or "Disruptor" or "Pulse" or "MortarPayload" or "MortarSupport"))
            yield return L.T("MT_SpecialistConfigKind");
        if (!Positive(explosionRadius) || !Positive(warmupFactor) || !Positive(cooldownFactor) || !Positive(burstCountFactor)
            || !Positive(minimumArmorPenetration) || !Positive(limbDamageFactor)
            || !Positive(damageFactor) || !Positive(explosionRadiusFactor) || !Positive(autoReloadRadius) || autoReloadRadius > 40f
            || !Probability(penetrationDamageFactor) || !Probability(panicChance) || !Probability(limbDamageChance)
            || !Probability(internalDamageFactor) || penetrationTargets < 1 || penetrationTargets > 32 || panicTicks < 1
            || stunTicks < 1 || stunRecoveryTicks <= stunTicks || kind == "Pulse" && stunRecoveryHediff == null)
            yield return L.T("MT_SpecialistConfigValues");
    }
    private static bool Positive(float value) => float.IsFinite(value) && value > 0f;
    private static bool Probability(float value) => float.IsFinite(value) && value >= 0f && value <= 1f;
    internal static float Factor(float value, float fallback) => Positive(value) ? value : fallback;
}
