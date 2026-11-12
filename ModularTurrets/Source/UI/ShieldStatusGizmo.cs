using System;
using UnityEngine;
using Verse;

namespace ModularTurrets;

public sealed partial class Building_ModularTurret
{
    public float CurrentShieldEnergy => Math.Clamp(shieldEnergy, 0f, ShieldMaximum);
    public int ShieldRechargeDelayTicks => Math.Max(0, shieldDelay);
    public int? ShieldEmergencyCooldownTicks
    {
        get
        {
            var comp = ActiveParagon("Shield")?.TryGetComp<CompTurretPart>();
            return comp == null ? null : (int)Math.Clamp((long)comp.EmergencyShieldReadyTick
                - (Find.TickManager?.TicksGame ?? 0), 0L, int.MaxValue);
        }
    }
}
