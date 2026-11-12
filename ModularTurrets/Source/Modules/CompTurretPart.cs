using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace ModularTurrets;

public sealed partial class CompTurretPart : ThingComp
{
    public int Tier;
    public bool IsParagon;
    public int EmergencyShieldReadyTick;

    internal void ObserveShieldCombat(int tick, int quietTicks)
    {
        // Once ready, the first hit of a new engagement must still be able to trigger it.
        if (tick < EmergencyShieldReadyTick)
            EmergencyShieldReadyTick = System.Math.Max(EmergencyShieldReadyTick, ShieldReadyAfter(tick, quietTicks));
    }

    internal static int ShieldReadyAfter(int tick, int quietTicks)
        => (int)System.Math.Min(int.MaxValue, (long)tick + System.Math.Max(60, quietTicks));

    public override void PostExposeData()
    {
        base.PostExposeData();
        ExposeUpgradeInvestment();
        Scribe_Values.Look(ref Tier, "modularTier", 0);
        Scribe_Values.Look(ref IsParagon, "modularParagon", false);
        Scribe_Values.Look(ref EmergencyShieldReadyTick, "paragonEmergencyShieldReadyTick", 0);
        if (Scribe.mode == LoadSaveMode.PostLoadInit)
        {
            IsParagon &= Tier == 5 && ParagonUtility.IsEligible(parent.def);
            Tier = System.Math.Clamp(Tier, 0, 5);
        }
    }
    public override string TransformLabel(string label) => ParagonUtility.IsParagon(parent) ? L.T("MT_ParagonPartLabel", label) : label;
    public override string CompInspectStringExtra() => ParagonUtility.IsParagon(parent)
        ? L.T(ParagonMapRegistry.IsEffective(parent) ? "MT_ParagonInspect" : "MT_ParagonInactive")
        : !Building_ModularTurret.CanUpgradePart(parent.def)
        ? L.T("MT_SpecialistFixedTier") : L.T("MT_TierInspect", Tier);
    public override bool AllowStackWith(Thing other) => (other.TryGetComp<CompTurretPart>()?.Tier ?? 0) == Tier
        && (other.TryGetComp<CompTurretPart>()?.IsParagon ?? false) == IsParagon;
    public override void PostSpawnSetup(bool respawningAfterLoad)
    {
        base.PostSpawnSetup(respawningAfterLoad);
        ParagonMapRegistry.Dirty(parent.Map);
    }
    public override void PostDeSpawn(Map map, DestroyMode mode = DestroyMode.Vanish)
    {
        base.PostDeSpawn(map, mode);
        ParagonMapRegistry.Dirty(map);
    }
    public override void PostDestroy(DestroyMode mode, Map previousMap)
    {
        base.PostDestroy(mode, previousMap);
        ParagonMapRegistry.Dirty(previousMap ?? parent.MapHeld);
    }
}
