using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace ModularTurrets;

public sealed partial class Building_ModularTurret
{
    public SpecialistModuleExtension ActiveSpecialist
    {
        get
        {
            ArgumentNullException.ThrowIfNull(fitted, "source");
            foreach (var m in fitted)
            {
                if (m.Slot == "head" || m.Part == null || !CanMountPart(m.Slot, m.Part.def)) continue;
                var specialist = m.Part.def.GetModExtension<SpecialistModuleExtension>();
                if (specialist != null) return specialist;
            }
            return null;
        }
    }

    public SpecialistModuleExtension SpecialistFor(Thing equipment)
        => equipment != null && channels.Any(c => c.Slot == "head" && c.Gun == equipment) ? ActiveSpecialist : null;

    public float MainCooldownSeconds
        => MainCooldownTicks(Head, ActiveSpecialist) / 60f;

    private int MainCooldownTicks(PartExtension head, SpecialistModuleExtension specialist)
    {
        float factor = specialist?.kind == "Cycle" ? SpecialistModuleExtension.Factor(specialist.cooldownFactor, 1f) : 1f;
        return Math.Max(1, Mathf.RoundToInt((head?.cooldown ?? 1f) * CoolingMultiplier * factor * MortarSupportCooldownFactor * 60f));
    }
}
