using System;
using System.Linq;
using Verse;

namespace ModularTurrets;

public sealed partial class Building_ModularTurret
{
    public Thing ActiveParagon(string kind)
    {
        // Registry queries may refresh configuration; retain the List enumerator
        // and the original first-match order across those calls.
        foreach (var mount in fitted)
        {
            Thing part = mount.Part;
            if (part.def.GetModExtension<PartExtension>()?.moduleKind == kind
                && ParagonUtility.IsParagon(part) && ParagonMapRegistry.IsEffective(part)) return part;
        }
        return null;
    }

    private static float ParagonFraction(float value, float fallback)
        => float.IsFinite(value) && value > 0f ? Math.Clamp(value, 0.01f, 1f) : fallback;

    private int TargetScanInterval => ActiveParagon("Range") is { } radar
        ? Math.Clamp(ParagonUtility.Settings(radar.def).detectionIntervalTicks, 1, 15) : 15;

    internal void NoteShieldCombatActivity(int tick)
    {
        foreach (var mount in fitted)
        {
            var part = mount.Part;
            var comp = part?.TryGetComp<CompTurretPart>();
            if (comp == null || !comp.IsParagon || tick >= comp.EmergencyShieldReadyTick) continue;
            if (part.def.GetModExtension<PartExtension>()?.moduleKind == "Shield")
                comp.ObserveShieldCombat(tick, ParagonUtility.Settings(part.def).emergencyShieldCooldownTicks);
        }
    }

    private void TryEmergencyShield()
    {
        Thing part = ActiveParagon("Shield");
        if (part == null || !Powered) return;
        var comp = part.TryGetComp<CompTurretPart>();
        int tick = Find.TickManager.TicksGame;
        if (tick < comp.EmergencyShieldReadyTick) return;
        var settings = ParagonUtility.Settings(part.def);
        float fraction = float.IsFinite(settings.emergencyShieldFraction)
            ? Math.Clamp(settings.emergencyShieldFraction, 0f, 0.2f) : 0.2f;
        // The triggering attack's remaining damage has already been calculated.
        // Restored energy only protects later attacks; EMP never enters this path.
        shieldEnergy = ShieldMaximum * fraction;
        shieldDelay = 0;
        comp.EmergencyShieldReadyTick = CompTurretPart.ShieldReadyAfter(tick, settings.emergencyShieldCooldownTicks);
    }
}
