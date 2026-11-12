using System.Collections.Generic;
using ModularTurrets.Batteries;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace ModularTurrets;

public sealed partial class Building_ModularTurret
{
    private bool selfPowered;
    private CompProperties_Power gridProperties, internalProperties;
    internal bool HasInternalSupply => selfPowered;
    private bool InternalSupplyAvailable => !IsStunned && !this.IsBrokenDown()
        && FlickUtility.WantsToBeOn(this) && !Map.gameConditionManager.ElectricityDisabled(Map);

    private void RefreshPowerSource()
    {
        bool previous = selfPowered;
        selfPowered = false;
        foreach (var mount in fitted)
            if (mount.Slot != "head" && mount.Part is { Destroyed: false } part
                && part.def.GetModExtension<PartExtension>()?.moduleKind == "SelfPower" && CanMountPart(mount.Slot, part.def))
            { selfPowered = true; break; }
        if (GetComp<CompPowerTrader>() is not { } trader) return;
        if (selfPowered && trader.props != internalProperties)
        {
            gridProperties = trader.Props;
            // Core requires the exact vanilla trader class for ordinary connectors.
            // Only this instance uses zero draw while the internal supply is fitted.
            internalProperties = new CompProperties_Power
            {
                compClass = gridProperties.compClass,
                showPowerNeededIfOff = false,
                shortCircuitInRain = gridProperties.shortCircuitInRain,
                soundPowerOn = gridProperties.soundPowerOn,
                soundPowerOff = gridProperties.soundPowerOff,
                soundAmbientPowered = gridProperties.soundAmbientPowered,
                soundAmbientProducingPower = gridProperties.soundAmbientProducingPower
            };
            trader.props = internalProperties;
            BatteryWireless.ForgetWirelessConnection(trader);
        }
        else if (!selfPowered && trader.props == internalProperties && gridProperties != null)
            trader.props = gridProperties;
        if (previous && !selfPowered && Spawned)
        {
            trader.PowerOn = false;
            if (trader.connectParent == null) Map.powerNetManager.Notify_ConnectorWantsConnect(trader);
        }
    }

    private IEnumerable<Gizmo> GridGizmos(IEnumerable<Gizmo> original)
    {
        string label = "CommandTryReconnectLabel".Translate();
        foreach (var gizmo in original)
        {
            if (gizmo is Command_Action command && command.hotKey == KeyBindingDefOf.Misc2
                && command.defaultLabel == label) continue;
            yield return gizmo;
        }
        if (!Spawned || Faction != Faction.OfPlayer || IsWrecked || selfPowered) yield break;
        yield return new Command_Action
        {
            defaultLabel = label, defaultDesc = L.T("MT_TurretReconnectPowerDesc"),
            icon = ContentFinder<Texture2D>.Get("UI/Commands/TryReconnect"), hotKey = KeyBindingDefOf.Misc2,
            action = () =>
            {
                SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
                BatteryWireless.ReconnectTurret(this);
            }
        };
    }
}
