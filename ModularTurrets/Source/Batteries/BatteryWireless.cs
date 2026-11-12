using System;
using RimWorld;
using Verse;

namespace ModularTurrets.Batteries;

internal static class BatteryWireless
{
    // Wireless supply uses real power-net connections. Keep the source's floor
    // scope around connection changes so another floor cannot receive this power.
    private static bool SameFloor(Thing consumer, int floor)
        => FloorCompatibility.TryGetFloor(consumer, out int consumerFloor) && floor == consumerFloor;

    private static bool Within(CompModularBattery source, Thing receiver)
        => source.parent.Position.DistanceToSquared(receiver.Position) <= source.WirelessRadius * source.WirelessRadius;

    internal static void ForgetWirelessConnection(CompPower receiver)
    {
        if (receiver.connectParent is CompModularBattery source && source.WirelessReceivers?.Remove(receiver.parent) == true)
        {
            PowerConnectionMaker.DisconnectFromPowerNet(receiver);
            Dirty(receiver.parent);
        }
    }

    private static void Attach(CompPowerTrader consumer, CompModularBattery source)
    {
        consumer.ConnectToTransmitter(source);
        var receivers = source.WirelessReceivers ??= new();
        if (!receivers.Contains(consumer.parent)) receivers.Add(consumer.parent);
        Dirty(consumer.parent);
    }

    internal static void ReconnectTurret(Building_ModularTurret turret)
    {
        if (!turret.Spawned || turret.Faction != Faction.OfPlayer || turret.HasInternalSupply || turret.IsWrecked
            || turret.PowerComp is not CompPowerTrader trader || !FloorCompatibility.TryGetFloor(turret, out int floor)
            || !FloorCompatibility.TryEnter(turret.Map, floor, out var scope)) return;
        using (scope)
        {
            Map map = turret.Map;
            if (WiredTransmitter(turret, map, floor) is CompPower wired && CanUseWire(wired, trader))
            {
                ForgetWirelessConnection(trader);
                trader.TryManualReconnect();
                if (trader.connectParent is { } next && SameFloor(next.parent, floor) && CanUseWire(next, trader)) return;
                PowerConnectionMaker.DisconnectFromPowerNet(trader);
                trader.ConnectToTransmitter(wired);
                Dirty(turret);
                return;
            }
            CompModularBattery best = null;
            int bestDistance = int.MaxValue;
            bool bestHasPower = false;
            var definition = DefDatabase<ThingDef>.GetNamedSilentFail("MT_ModularBattery");
            if (definition != null)
                foreach (var thing in map.listerThings.ThingsOfDef(definition))
                {
                    if (thing is not Building_ModularBattery battery || !battery.Spawned || battery.Map != map
                        || battery.Faction != turret.Faction || !SameFloor(battery, floor)) continue;
                    var source = battery.Battery;
                    if (source == null || source.WirelessRadius <= 0f || !Within(source, turret) || source.PowerNet == null
                        || !source.TransmitsPowerNow || battery.IsBrokenDown() || source.StunnedByEMP) continue;
                    int distance = turret.Position.DistanceToSquared(battery.Position);
                    bool hasPower = source.PowerNet.HasActivePowerSource;
                    if (best != null && (bestHasPower && !hasPower || hasPower == bestHasPower
                        && (distance > bestDistance || distance == bestDistance && battery.thingIDNumber >= best.parent.thingIDNumber))) continue;
                    best = source;
                    bestHasPower = hasPower;
                    bestDistance = distance;
                }
            if (best != null && trader.connectParent == best && best.WirelessReceivers?.Contains(turret) == true) return;
            ForgetWirelessConnection(trader);
            if (best == null)
            {
                if (trader.connectParent == null) trader.LostConnectParent();
                return;
            }
            PowerConnectionMaker.DisconnectFromPowerNet(trader);
            Attach(trader, best);
        }
    }

    private static void Dirty(Thing receiver)
    {
        if (!receiver.Spawned) return;
        receiver.Map.mapDrawer.MapMeshDirty(receiver.Position, MapMeshFlagDefOf.Things);
        receiver.Map.mapDrawer.MapMeshDirty(receiver.Position, MapMeshFlagDefOf.PowerGrid);
    }

    private static void Release(CompPower receiver)
    {
        PowerConnectionMaker.DisconnectFromPowerNet(receiver);
        var building = receiver.parent;
        if (building.Spawned && FloorCompatibility.TryGetFloor(building, out int floor)
            && FloorCompatibility.TryEnter(building.Map, floor, out var scope))
        {
            using (scope)
            {
                receiver.LostConnectParent();
                Dirty(building);
            }
        }
        else if (receiver is CompPowerTrader trader) trader.PowerOn = false;
    }

    internal static void ReleaseRemoteLinks(CompModularBattery source)
    {
        var owned = source.WirelessReceivers;
        if (owned == null) return;
        foreach (var receiver in owned)
            if (receiver is Building building && building.PowerComp is CompPower child && child.connectParent == source)
                Release(child);
        owned.Clear();
    }

    internal static void RestoreRemoteLinks(CompModularBattery source)
    {
        // Core restores connector parents before PostSwapMap, after DeSpawn cleared
        // our ownership list. Reclaim only remote children, never a physical wire.
        ThingWithComps emitter = source.parent;
        if (source.connectChildren == null || source.WirelessRadius <= 0f || !emitter.Spawned
            || emitter.Faction != Faction.OfPlayer || emitter.IsBrokenDown() || source.StunnedByEMP
            || source.PowerNet == null || !source.TransmitsPowerNow
            || !FloorCompatibility.TryGetFloor(emitter, out int floor)
            || !FloorCompatibility.TryEnter(emitter.Map, floor, out var scope)) return;
        using (scope)
            foreach (var child in source.connectChildren)
            {
                if (child is not CompPowerTrader trader || child.connectParent != source
                    || trader.parent is not Building receiver || !receiver.Spawned || receiver.Map != emitter.Map
                    || receiver.Faction != emitter.Faction || !receiver.def.ConnectToPower
                    || trader.Props.PowerConsumption <= 0f || !Within(source, receiver) || !SameFloor(receiver, floor)
                    || receiver is Building_ModularTurret { HasInternalSupply: true }
                    || source.WirelessReceivers?.Contains(receiver) == true
                    || WithinWiredReach(receiver, source)) continue;
                (source.WirelessReceivers ??= new()).Add(receiver);
                Dirty(receiver);
            }
    }

    internal static void Refresh(CompModularBattery source)
    {
        ThingWithComps emitter = source.parent;
        if (source.WirelessRadius <= 0f || !emitter.Spawned || emitter.Faction != Faction.OfPlayer
            || emitter.IsBrokenDown() || source.StunnedByEMP || !FloorCompatibility.TryGetFloor(emitter, out int floor)
            || !FloorCompatibility.TryEnter(emitter.Map, floor, out var scope))
        {
            ReleaseRemoteLinks(source);
            return;
        }
        using (scope)
        {
            Map map = emitter.Map;
            var owned = source.WirelessReceivers;
            bool preferWire = emitter.IsHashIntervalTick(2500);
            if (owned != null)
                for (int i = owned.Count - 1; i >= 0; i--)
                {
                    if (owned[i] is not Building receiver || receiver.PowerComp is not CompPower child
                        || child.connectParent != source)
                    { owned.RemoveAt(i); continue; }
                    if (!receiver.Spawned || receiver.Map != map || receiver.Faction != emitter.Faction
                        || !SameFloor(receiver, floor) || !Within(source, receiver)
                        || receiver is Building_ModularTurret { HasInternalSupply: true })
                    { owned.RemoveAt(i); Release(child); continue; }
                    if (preferWire && WiredTransmitter(receiver, map, floor) is CompPower wired
                        && CanUseWire(wired, child as CompPowerTrader))
                    {
                        owned.RemoveAt(i);
                        PowerConnectionMaker.DisconnectFromPowerNet(child);
                        child.ConnectToTransmitter(wired);
                        Dirty(receiver);
                    }
                }
            if (source.PowerNet == null || !source.TransmitsPowerNow) return;
            foreach (Building receiver in map.listerBuildings.allBuildingsColonist)
            {
                if (!receiver.Spawned || receiver.Map != map || receiver.Faction != emitter.Faction
                    || !Within(source, receiver) || !receiver.def.ConnectToPower
                    || receiver.PowerComp is not CompPowerTrader trader || trader.Props.PowerConsumption <= 0f
                    || !SameFloor(receiver, floor)) continue;
                // Keep working wires and other coils. An unpowered wire island
                // must not prevent a coil from supplying ordinary auxiliary loads.
                if (trader.connectParent is CompModularBattery || trader.PowerNet == source.PowerNet
                    || trader.PowerNet != null && CanSupply(trader.PowerNet, trader)) continue;
                if (WiredTransmitter(receiver, map, floor) is { } wired && CanUseWire(wired, trader)) continue;
                if (trader.connectParent != null || trader.PowerNet != null)
                {
                    if (!CanSupply(source.PowerNet, trader)) continue;
                    PowerConnectionMaker.DisconnectFromPowerNet(trader);
                }
                Attach(trader, source);
            }
        }
    }

    private static bool CanUseWire(CompPower transmitter, CompPowerTrader receiver)
        => transmitter?.PowerNet is { } net && receiver != null
            && CanSupply(net, receiver);

    internal static bool CanSupply(PowerNet net, CompPowerTrader receiver)
    {
        if (net == null || receiver == null) return false;
        if (DebugSettings.unlimitedPower) return true;
        float stored = net.CurrentStoredEnergy();
        float running = stored + net.CurrentEnergyGainRate();
        if (running < 0f) return false;
        // Charged nets already supply their running members; restart demand only
        // matters for off or new receivers. Keep zero-storage/custom sources below.
        if (stored > 0f && float.IsFinite(running) && receiver.PowerOn)
            for (int i = 0; i < net.powerComps.Count; i++)
                if (ReferenceEquals(net.powerComps[i], receiver)) return true;
        bool hasSupply = stored > 0f;
        bool included = false;
        float waitingDemand = 0f;
        foreach (var load in net.powerComps)
        {
            if (ReferenceEquals(load, receiver)) included = true;
            if (load.PowerOn && load.EnergyOutputPerTick > 0f) hasSupply = true;
            if (!load.PowerOn && load.EnergyOutputPerTick < 0f
                && FlickUtility.WantsToBeOn(load.parent) && !load.parent.IsBrokenDown())
                waitingDemand += load.EnergyOutputPerTick;
        }
        // A psychic effect may force PowerOn without supplying any electricity.
        if (!hasSupply || !float.IsFinite(running)) return false;
        if (included && receiver.PowerOn) return true;
        float available = running + waitingDemand;
        // Match Core's restart reserve, including its low-charge boundary.
        if (net.batteryComps.Count > 0 && stored >= 0.1f) available -= 5f;
        // Reserve demand for attached loads that Core has not restarted yet,
        // so sequential wire hand-offs cannot all claim the same spare output.
        if (!included) available += Math.Min(0f, receiver.EnergyOutputPerTick);
        return float.IsFinite(available) && available >= 0f;
    }

    private static bool WithinWiredReach(Thing receiver, CompPower source)
    {
        Thing anchor = receiver.def.building.isAttachment ? GenConstruct.GetWallAttachedTo(receiver) : receiver;
        // Core's connector search uses a six-cell square around the anchor and
        // tests transmitter footprints. A different, nearer wire is irrelevant.
        return anchor != null && source.parent.def.building?.allowWireConnection != false
            && source.parent.OccupiedRect().ExpandedBy(6).Contains(anchor.Position);
    }

    private static CompPower WiredTransmitter(Thing receiver, Map map, int floor)
    {
        Thing anchor = receiver.def.building.isAttachment ? GenConstruct.GetWallAttachedTo(receiver) : receiver;
        if (anchor == null) return null;
        var transmitter = PowerConnectionMaker.BestTransmitterForConnector(anchor.Position, map);
        return transmitter != null && SameFloor(transmitter.parent, floor) ? transmitter : null;
    }
}
