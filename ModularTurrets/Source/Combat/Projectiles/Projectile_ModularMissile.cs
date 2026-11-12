using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace ModularTurrets;

public class Projectile_ModularMissile : Projectile_ModularExplosive
{
    private static int fallbackLane;
    private Vector3 bend;
    private int flightTicks, smokeInterval = 2;
    private float smokeSize = 0.35f;
    private bool curveInitialized;

    // Offscreen projectiles normally update every 15 ticks: that would cut corners.
    public override int UpdateRateTicks => 1;
    private float Progress => Mathf.Clamp01(1f - (float)ticksToImpact / flightTicks);

    public override Vector3 ExactPosition
    {
        get
        {
            if (!curveInitialized || flightTicks < 1) return base.ExactPosition;
            if (landed) return Position.ToVector3Shifted().Yto0() + Vector3.up * def.Altitude;
            float t = Progress;
            return origin.Yto0() + (destination - origin).Yto0() * t
                + bend * (6.75f * t * (1f - t) * (1f - t)) + Vector3.up * def.Altitude;
        }
    }

    public override Quaternion ExactRotation
    {
        get
        {
            if (!curveInitialized || flightTicks < 1) return base.ExactRotation;
            float t = Progress;
            Vector3 tangent = (destination - origin).Yto0() + bend * (6.75f * (1f - t) * (1f - 3f * t));
            return tangent.sqrMagnitude > 0.000001f ? Quaternion.LookRotation(tangent) : Quaternion.identity;
        }
    }

    public override void Launch(Thing launcher, Vector3 origin, LocalTargetInfo usedTarget, LocalTargetInfo intendedTarget,
        ProjectileHitFlags hitFlags, bool preventFriendlyFire = false, Thing equipment = null, ThingDef targetCoverDef = null)
    {
        curveInitialized = false;
        base.Launch(launcher, origin, usedTarget, intendedTarget, hitFlags, preventFriendlyFire, equipment, targetCoverDef);
        // Base Launch already selects the vanilla miss/cover/branch landing point.
        // Do not replace destination with the intended pawn or update it in flight.
        var settings = def.GetModExtension<MissileFlightExtension>() ?? new MissileFlightExtension();
        Vector3 direction = (destination - this.origin).Yto0();
        float distance = direction.magnitude;
        float width = Math.Min(Nonnegative(settings.curveWidth, 4.5f),
            distance * Nonnegative(settings.distanceWidthLimit, 0.25f));
        int lane = launcher is Building_ModularTurret turret ? turret.TakeMissileLane() : fallbackLane++ & 7;
        float sign = (lane & 1) == 0 ? -1f : 1f;
        float laneWidth = 0.65f + 0.35f * (lane / 2) / 3f;
        bend = distance > 0.001f ? new Vector3(-direction.z, 0f, direction.x) / distance
            * (width * sign * laneWidth * Rand.Range(0.92f, 1f)) : Vector3.zero;
        if (Map != null) bend = LimitBend(this.origin, destination, bend, Map.Size.x, Map.Size.z);
        float multiplier = float.IsFinite(settings.durationMultiplier) && settings.durationMultiplier > 0f
            ? settings.durationMultiplier : 1.35f;
        flightTicks = (int)Math.Clamp(Math.Ceiling(ticksToImpact * (double)multiplier), 1d, int.MaxValue);
        ticksToImpact = lifetime = flightTicks;
        smokeInterval = Math.Max(1, settings.smokeInterval);
        smokeSize = Nonnegative(settings.smokeSize, 0.35f);
        curveInitialized = true;
    }

    private static float Nonnegative(float value, float fallback) => float.IsFinite(value) && value >= 0f ? value : fallback;

    private static Vector3 LimitBend(Vector3 from, Vector3 to, Vector3 offset, int width, int height)
    {
        if (width < 1 || height < 1 || from.x < 0f || from.z < 0f || from.x >= width || from.z >= height
            || to.x < 0f || to.z < 0f || to.x >= width || to.z >= height) return Vector3.zero;
        // Equivalent cubic controls: P1=from+(to-from)/3+2.25*bend,
        // P2=from+2*(to-from)/3. Keeping P1 in bounds bounds the whole curve.
        Vector3 control = from + (to - from) / 3f;
        Vector3 delta = offset * 2.25f;
        float scale = 1f;
        if (delta.x > 0f) scale = Math.Min(scale, (width - 0.001f - control.x) / delta.x);
        else if (delta.x < 0f) scale = Math.Min(scale, (0.001f - control.x) / delta.x);
        if (delta.z > 0f) scale = Math.Min(scale, (height - 0.001f - control.z) / delta.z);
        else if (delta.z < 0f) scale = Math.Min(scale, (0.001f - control.z) / delta.z);
        return offset * Mathf.Clamp01(scale);
    }

    protected override void TickInterval(int delta)
    {
        // A restored Thing may carry a pending interval >1 even with UpdateRateTicks=1.
        for (int i = 0; i < delta; i++)
        {
            if (Destroyed || !Spawned) return;
            Vector3 previous = ExactPosition;
            base.TickInterval(1); // Actual cell movement, walls, cover, shields and one impact.
            if (Destroyed || !Spawned) return;
            if (curveInitialized && !landed && smokeSize > 0f
                && (flightTicks - ticksToImpact) % smokeInterval == 0 && previous.InBounds(Map))
                FleckMaker.ThrowSmoke(previous, Map, smokeSize);
        }
    }

    public override void ExposeData()
    {
        base.ExposeData();
        Scribe_Values.Look(ref curveInitialized, "modularCurveInitialized", false);
        Scribe_Values.Look(ref bend, "modularMissileBend");
        Scribe_Values.Look(ref flightTicks, "modularMissileFlightTicks");
        Scribe_Values.Look(ref smokeInterval, "modularMissileSmokeInterval", 2);
        Scribe_Values.Look(ref smokeSize, "modularMissileSmokeSize", 0.35f);
        if (Scribe.mode == LoadSaveMode.PostLoadInit)
        {
            if (flightTicks < 1 || !float.IsFinite(bend.x) || !float.IsFinite(bend.z)) curveInitialized = false;
            smokeInterval = Math.Max(1, smokeInterval);
            smokeSize = Nonnegative(smokeSize, 0.35f);
        }
    }
}
