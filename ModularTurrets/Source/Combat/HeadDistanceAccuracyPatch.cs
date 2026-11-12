using System;
using HarmonyLib;
using Verse;

namespace ModularTurrets;

[StaticConstructorOnStartup]
internal static class HeadDistanceAccuracyPatch
{
    private static AccessTools.StructFieldRef<ShotReport, float> shooterFactor;
    private static AccessTools.StructFieldRef<ShotReport, float> weaponFactor;
    private static bool warned;

    static HeadDistanceAccuracyPatch()
    {
        try
        {
            var field = AccessTools.DeclaredField(typeof(ShotReport), "factorFromShooterAndDist");
            var weapon = AccessTools.DeclaredField(typeof(ShotReport), "factorFromEquipment");
            var target = AccessTools.DeclaredMethod(typeof(ShotReport), nameof(ShotReport.HitReportFor),
                new[] { typeof(Thing), typeof(Verb), typeof(LocalTargetInfo) });
            if (!typeof(ShotReport).IsValueType || field?.FieldType != typeof(float) || field.IsStatic
                || weapon?.FieldType != typeof(float) || weapon.IsStatic
                || target?.ReturnType != typeof(ShotReport) || !target.IsStatic)
                throw new MissingMemberException("Native ShotReport shooter/distance API changed.");
            shooterFactor = AccessTools.StructFieldRefAccess<ShotReport, float>(field);
            weaponFactor = AccessTools.StructFieldRefAccess<ShotReport, float>(weapon);
            ShotReport probe = default;
            shooterFactor(ref probe) = 0.5f;
            weaponFactor(ref probe) = 0.25f;
            if (shooterFactor(ref probe) != 0.5f || weaponFactor(ref probe) != 0.25f)
                throw new InvalidOperationException("Native ShotReport field binding failed.");
            new Harmony("local.modularturrets.head-distance-accuracy").Patch(target,
                postfix: new HarmonyMethod(typeof(HeadDistanceAccuracyPatch), nameof(AdjustReport)));
        }
        catch (Exception error) { Disable(error); }
    }

    private static void AdjustReport(Thing __0, Verb __1, LocalTargetInfo __2, ref ShotReport __result)
    {
        if (__0 is not Building_ModularTurret turret) return;
        if (!turret.Spawned) return;
        if (shooterFactor == null || weaponFactor == null || __1?.verbProps == null || !__2.IsValid) return;
        try
        {
            if (!CombatCompatibility.IsAvailable || CombatCompatibility.Provider != null) return;
            var equipment = __1.EquipmentSource;
            if (!turret.IsCurrentHeadAccuracyWeapon(equipment)) return;
            float distance = (__2.Cell - turret.Position).LengthHorizontal;
            float range = __1.EffectiveRange;
            if (!float.IsFinite(range) || range <= 0f || !float.IsFinite(distance)
                || distance < __1.verbProps.minRange || distance > range) return;
            float currentWeapon = __1.verbProps.GetHitChanceFactor(equipment, distance);
            float maximumRangeWeapon = __1.verbProps.GetHitChanceFactor(equipment, range);
            TryAdjust(ref __result, currentWeapon, maximumRangeWeapon);
        }
        catch (Exception error) { Disable(error); }
    }

    internal static bool TryAdjust(ref ShotReport report, float currentWeapon, float maximumRangeWeapon)
    {
        if (shooterFactor == null || weaponFactor == null || !float.IsFinite(currentWeapon) || currentWeapon <= 0f || currentWeapon > 1f
            || !float.IsFinite(maximumRangeWeapon) || maximumRangeWeapon < 0f || maximumRangeWeapon > 1f) return false;
        ShotReport original = report;
        try
        {
            ref float factor = ref shooterFactor(ref report);
            ref float weapon = ref weaponFactor(ref report);
            if (!float.IsFinite(factor) || factor < 0f || factor > 1f || !float.IsFinite(weapon) || weapon < 0f) return false;
            double adjustedWeapon = (double)weapon * maximumRangeWeapon / currentWeapon;
            if (!double.IsFinite(adjustedWeapon) || adjustedWeapon > float.MaxValue) return false;
            // Preserve other report equipment modifiers while flattening only the native weapon curve.
            weapon = (float)adjustedWeapon;
            factor = 1f;
            return true;
        }
        catch (Exception error)
        {
            report = original;
            Disable(error);
            return false;
        }
    }

    private static void Disable(Exception error)
    {
        shooterFactor = null;
        weaponFactor = null;
        if (warned) return;
        warned = true;
        ModDiagnostics.WarningOnce("Combat.HeadDistanceAccuracyDisabled", error);
    }
}
