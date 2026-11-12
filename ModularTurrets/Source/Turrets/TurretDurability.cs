using System;
using RimWorld;
using Verse;

namespace ModularTurrets;

public sealed partial class Building_ModularTurret
{
    // Saved maximum distinguishes an existing 360-HP save from a newly made base.
    private int durabilityMaxHitPoints;

    public int InstalledDurabilityBonus(int headHitPoints, int moduleHitPoints, int moduleTierHitPoints)
    {
        int result = 0;
        if (fitted == null) return result;
        foreach (var mount in fitted)
        {
            var part = mount?.Part;
            var definition = part?.def.GetModExtension<PartExtension>();
            if (definition == null) continue;
            if (mount.Slot == "head")
            {
                if (definition.isHead) result += headHitPoints;
            }
            else if (!definition.isHead)
            {
                // A paragon remains tier V; fixed dedicated modules have no tiers.
                int tier = part.def.GetModExtension<SpecialistModuleExtension>() == null
                    ? Math.Clamp(part.TryGetComp<CompTurretPart>()?.Tier ?? 0, 0, 5) : 0;
                result += moduleHitPoints + moduleTierHitPoints * tier;
            }
        }
        return result;
    }

    private void ExposeDurability()
    {
        Scribe_Values.Look(ref durabilityMaxHitPoints, "modularDurabilityMaxHitPoints", 0);
        if (Scribe.mode == LoadSaveMode.PostLoadInit && durabilityMaxHitPoints <= 0)
            durabilityMaxHitPoints = 360;
    }

    private void SynchronizeDurability()
    {
        // Bypass and refresh Core's ten-tick MaxHitPoints cache after a mount change.
        int maximum = Math.Max(1, (int)MathF.Round(this.GetStatValue(StatDefOf.MaxHitPoints, true, -1)));
        if (IsWrecked) return;
        if (durabilityMaxHitPoints > 0 && durabilityMaxHitPoints != maximum && HitPoints > 0)
        {
            // Round down: removing/reinstalling a module cannot repeatedly heal damage.
            long scaled = (long)Math.Min(HitPoints, durabilityMaxHitPoints) * maximum / durabilityMaxHitPoints;
            HitPoints = (int)Math.Clamp(scaled, 1L, maximum);
        }
        else if (HitPoints > maximum) HitPoints = maximum;
        durabilityMaxHitPoints = maximum;
    }

    internal void RestoreDurabilityForStage()
    {
        int maximum = Math.Max(1, (int)MathF.Round(this.GetStatValue(StatDefOf.MaxHitPoints, true, -1)));
        HitPoints = maximum;
        if (!IsWrecked) durabilityMaxHitPoints = maximum;
    }
}
