using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace ModularTurrets.Batteries;

public sealed partial class Building_ModularBattery : Building_Battery, IThingHolder
{
    private ThingOwner<Thing> contents;
    private Thing leftModule, rightModule;
    private CompModularBattery batteryComp;
    private int nextOrderId = 1, blockedReturnOrderId;
    public int CapacityLevel;
    public List<BatteryOrder> Orders = new();
    public IReadOnlyList<BatteryOrder> AssemblyOrders => Orders;
    public BatteryOrder PendingOrder => Orders is { Count: > 0 } ? Orders[0] : null;
    private ThingOwner<Thing> Contents => contents ??= new ThingOwner<Thing>(this);
    private bool IsStaged(Thing item) => item != leftModule && item != rightModule;
    public CompModularBattery Battery => batteryComp ??= GetComp<CompModularBattery>();
    public float Capacity => Battery?.Props.storedEnergyMax ?? 0f;
    public float ChargeEfficiency => Battery?.Props.efficiency ?? 0f;
    public bool AssemblyBlocked => blockedReturnOrderId != 0 && PendingOrder?.Id == blockedReturnOrderId;
    private bool CanQueue => Spawned && Faction == Faction.OfPlayer && Orders.Count < 64 && nextOrderId > 0 && nextOrderId < int.MaxValue;
    public bool CanQueueUpgrade
    {
        get
        {
            if (!CanQueue) return false;
            long level = CapacityLevel;
            foreach (var order in Orders)
                if (order.Kind == BatteryOperation.Upgrade) level++;
            return level < int.MaxValue;
        }
    }
    public bool CanReceive => PendingOrder is { } order && Spawned && Faction == Faction.OfPlayer
        && !AssemblyBlocked && OrderValid(order) && !this.IsBurning();
    public bool CanWork => CanReceive && MaterialsReady(PendingOrder);

    internal bool MaterialsReady(BatteryOrder order)
    {
        foreach (var cost in order.Costs)
            if (Needed(cost.thingDef) > 0) return false;
        return order.Kind != BatteryOperation.Install || FindStaged(order) != null;
    }

    internal bool MatchesInstallPart(BatteryOrder order, Thing part)
        => part.stackCount > 0 && part.def == order.PartDef && (order.PartId == 0 || order.PartId == part.thingIDNumber)
            && !WorkGiver_ModularBatteryAssembly.PartClaimedByOther(Map, part.thingIDNumber, order);

    internal bool BindCarriedInstallPart(Thing source, Thing carried, int orderId)
    {
        var order = PendingOrder;
        if (order?.Id != orderId || carried == null) return false;
        if (order.Kind != BatteryOperation.Install || order.PartId == 0 || carried == source) return true;
        return source?.thingIDNumber == order.PartId && carried.def == order.PartDef && carried.stackCount == 1
            && source.TryGetComp<CompBatteryModuleUpgrade>()?.Level == carried.TryGetComp<CompBatteryModuleUpgrade>()?.Level
            && WorkGiver_ModularBatteryAssembly.RebindPartClaim(Map, order, carried.thingIDNumber);
    }

    private Thing FindStaged(BatteryOrder order)
    {
        if (contents != null)
            foreach (Thing item in contents)
                if (IsStaged(item) && MatchesInstallPart(order, item)) return item;
        return null;
    }

    public Thing ModuleAt(string slot) => slot == "left" ? leftModule : slot == "right" ? rightModule : null;
    private static bool IsSlot(string slot) => slot is "left" or "right";
    private static bool IsModule(ThingDef part) => part?.GetModExtension<BatteryModuleExtension>() != null;
    private BatterySettingsExtension Settings => def.GetModExtension<BatterySettingsExtension>();
    private static float Positive(float value, float fallback) => float.IsFinite(value) && value > 0f ? value : fallback;
    private void SetModule(string slot, Thing part)
    {
        if (slot == "left") leftModule = part;
        else if (slot == "right") rightModule = part;
        else return;
        if (Spawned) DirtyMapMesh(Map);
    }

    public ThingOwner GetDirectlyHeldThings() => Contents;
    public void GetChildHolders(List<IThingHolder> outChildren) => ThingOwnerUtility.AppendThingHoldersFromThings(outChildren, Contents);

    public override void ExposeData()
    {
        // Cross references key on the parent Thing's hash, which changes when base reads its ID.
        if (Scribe.mode == LoadSaveMode.LoadingVars) batteryComp = null;
        base.ExposeData();
        Scribe_Values.Look(ref CapacityLevel, "batteryCapacityLevel", 0);
        Scribe_Deep.Look(ref contents, "batteryContents", this);
        Scribe_References.Look(ref leftModule, "batteryLeftModule");
        Scribe_References.Look(ref rightModule, "batteryRightModule");
        Scribe_Collections.Look(ref Orders, "batteryOrders", LookMode.Deep);
        Scribe_Values.Look(ref nextOrderId, "batteryNextOrderId", 1);
        Scribe_Values.Look(ref blockedReturnOrderId, "batteryBlockedReturnOrderId", 0);
        if (Scribe.mode == LoadSaveMode.PostLoadInit)
        {
            CapacityLevel = Math.Max(0, CapacityLevel);
            Orders ??= new();
            Orders.RemoveAll(static o => o == null);
            if (leftModule != null && FittedExtension(leftModule) == null) SetModule("left", null);
            if (rightModule != null && (rightModule == leftModule || FittedExtension(rightModule) == null)) SetModule("right", null);
            int highest = 0;
            bool hasBlockedOrder = false;
            foreach (var order in Orders)
            {
                highest = Math.Max(highest, order.Id);
                if (order.Id == blockedReturnOrderId && order.Kind == BatteryOperation.Remove && order.WorkLeft == 0f)
                    hasBlockedOrder = true;
            }
            nextOrderId = Math.Max(Math.Max(1, nextOrderId), highest == int.MaxValue ? int.MaxValue : highest + 1);
            if (!hasBlockedOrder) blockedReturnOrderId = 0;
            if (Spawned) SyncInstallClaims(Map, Faction == Faction.OfPlayer);
            UpdateWorkListing();
        }
    }
    public override void SpawnSetup(Map map, bool respawningAfterLoad)
    {
        base.SpawnSetup(map, respawningAfterLoad);
        SyncInstallClaims(map, Faction == Faction.OfPlayer);
        RefreshBatteryPower();
        UpdateWorkListing();
    }
    public override void DeSpawn(DestroyMode mode = DestroyMode.Vanish)
    {
        var previousMap = Map;
        SyncInstallClaims(previousMap, false);
        base.DeSpawn(mode);
        WorkGiver_ModularBatteryAssembly.SetPending(this, previousMap, false);
    }
    public override void SetFaction(Faction newFaction, Pawn recruiter = null)
    {
        base.SetFaction(newFaction, recruiter);
        if (Spawned) SyncInstallClaims(Map, newFaction == Faction.OfPlayer);
        RefreshBatteryPower();
        UpdateWorkListing();
    }
    private void UpdateWorkListing() => WorkGiver_ModularBatteryAssembly.SetPending(this, Map,
        Spawned && Faction == Faction.OfPlayer && PendingOrder != null && !AssemblyBlocked);

    private void SyncInstallClaims(Map map, bool claim)
    {
        if (Orders == null) return;
        foreach (var order in Orders) WorkGiver_ModularBatteryAssembly.SetPartClaim(map, order, claim);
    }

    private BatteryModuleExtension FittedExtension(Thing part)
        => part != null && !part.Destroyed && contents != null && contents.Contains(part)
            ? part.def.GetModExtension<BatteryModuleExtension>() : null;

    public void RefreshBatteryPower()
    {
        Battery?.RefreshFromBuilding(Math.Max(0, CapacityLevel), Positive(Settings?.capacityPerUpgrade ?? 1200f, 1200f),
            FittedExtension(leftModule), rightModule != leftModule ? FittedExtension(rightModule) : null,
            leftModule?.TryGetComp<CompBatteryModuleUpgrade>()?.Level ?? 0,
            rightModule?.TryGetComp<CompBatteryModuleUpgrade>()?.Level ?? 0);
    }
    public int Needed(ThingDef material)
    {
        var o = PendingOrder;
        if (o == null || material == null) return 0;
        if (o.Kind == BatteryOperation.Install && o.PartDef == material) return FindStaged(o) == null ? 1 : 0;
        long needed = 0;
        foreach (var cost in o.Costs)
            if (cost?.thingDef == material) needed += cost.count;
        if (needed > 0 && contents != null)
            foreach (Thing item in contents)
                if (item.def == material && IsStaged(item)) needed -= item.stackCount;
        return (int)Math.Clamp(needed, 0L, int.MaxValue);
    }
    public bool Wants(Thing thing)
    {
        var o = PendingOrder;
        if (o == null || thing == null || thing.Destroyed) return false;
        return (o.Kind != BatteryOperation.Install || MatchesInstallPart(o, thing)) && Needed(thing.def) > 0;
    }
    public int Receive(Thing carried, int orderId)
    {
        if (!CanReceive || PendingOrder.Id != orderId || !Wants(carried)) return 0;
        int count = PendingOrder.Kind == BatteryOperation.Install ? 1 : Math.Min(carried.stackCount, Needed(carried.def));
        return count > 0 ? Contents.TryAddOrTransfer(carried, count, PendingOrder.Kind != BatteryOperation.Install) : 0;
    }
    public bool WorkReady(int orderId) => PendingOrder?.Id == orderId && CanWork;
    private bool OrderValid(BatteryOrder order)
    {
        if (order.Id <= 0 || order.Costs == null) return false;
        foreach (var cost in order.Costs)
            if (cost?.thingDef == null || cost.count <= 0) return false;
        return order.Kind switch
        {
            BatteryOperation.Upgrade => CapacityLevel < int.MaxValue,
            BatteryOperation.Install => IsSlot(order.Slot) && ModuleAt(order.Slot) == null && IsModule(order.PartDef),
            BatteryOperation.Remove => IsSlot(order.Slot) && ModuleAt(order.Slot)?.thingIDNumber == order.PartId,
            BatteryOperation.UpgradeModule => OrderedModule(order)?.CanAdvanceTo(order.TargetLevel) == true,
            _ => false
        };
    }
    private bool AddOrder(BatteryOrder order)
    {
        if (!CanQueue || !WorkGiver_ModularBatteryAssembly.SetPartClaim(Map, order, true)) return false;
        order.Id = nextOrderId++;
        order.WorkLeft = order.WorkTotal;
        Orders.Add(order);
        if (Orders.Count == 1) UpdateWorkListing();
        return true;
    }
    public bool QueueUpgrade()
    {
        if (!CanQueueUpgrade) return false;
        var steel = DefDatabase<ThingDef>.GetNamedSilentFail("Steel");
        var components = DefDatabase<ThingDef>.GetNamedSilentFail("ComponentIndustrial");
        if (steel == null || components == null) return false;
        var settings = Settings;
        return AddOrder(new BatteryOrder { Kind = BatteryOperation.Upgrade,
            WorkTotal = Positive(settings?.upgradeWork ?? 600f, 600f),
            Costs = new() { new ThingDefCountClass(steel, settings?.upgradeSteel > 0 ? settings.upgradeSteel : 80),
                new ThingDefCountClass(components, settings?.upgradeComponents > 0 ? settings.upgradeComponents : 2) } });
    }
    public bool QueueInstall(string slot, ThingDef partDef)
        => QueueInstall(slot, partDef, 0);

    public bool CanQueueInstall(string slot, Thing part)
        => CanQueue && IsSlot(slot) && ModuleAt(slot) == null && !HasSlotOrder(slot)
            && part?.Spawned == true && part.Map == Map && part.stackCount > 0 && part.thingIDNumber > 0
            && IsModule(part.def) && !part.IsForbidden(Faction.OfPlayer) && !part.IsBurning()
            && !WorkGiver_ModularBatteryAssembly.PartClaimedByOther(Map, part.thingIDNumber);

    public bool QueueInstall(string slot, Thing part)
        => CanQueueInstall(slot, part) && QueueInstall(slot, part.def, part.thingIDNumber);

    private bool QueueInstall(string slot, ThingDef partDef, int partId)
    {
        if (!IsSlot(slot) || ModuleAt(slot) != null || !IsModule(partDef) || HasSlotOrder(slot)) return false;
        return AddOrder(new BatteryOrder { Kind = BatteryOperation.Install, Slot = slot, PartDef = partDef, PartId = partId,
            WorkTotal = Positive(Settings?.installWork ?? 100f, 100f) });
    }
    public bool QueueRemove(string slot)
    {
        var part = ModuleAt(slot);
        if (!IsSlot(slot) || part == null || HasSlotOrder(slot)) return false;
        return AddOrder(new BatteryOrder { Kind = BatteryOperation.Remove, Slot = slot, PartId = part.thingIDNumber,
            WorkTotal = Positive(Settings?.removeWork ?? 100f, 100f) });
    }
    private bool HasSlotOrder(string slot)
    {
        foreach (var order in Orders)
            if (order.Slot == slot) return true;
        return false;
    }
    public void DoWork(float amount, int orderId) => TryDoWork(amount, orderId);

    internal bool TryDoWork(float amount, int orderId)
    {
        if (!float.IsFinite(amount) || amount <= 0f || !WorkReady(orderId)) return false;
        var o = PendingOrder; o.WorkLeft = Math.Max(0f, o.WorkLeft - amount);
        if (o.WorkLeft > 0f) return true;
        if (o.Kind == BatteryOperation.Install) SetModule(o.Slot, FindStaged(o));
        else if (o.Kind == BatteryOperation.Remove)
        {
            var part = ModuleAt(o.Slot);
            if (!Contents.TryDrop(part, Position, Map, ThingPlaceMode.Near, out _))
            {
                blockedReturnOrderId = o.Id;
                UpdateWorkListing();
                return false;
            }
            SetModule(o.Slot, null);
        }
        else if (o.Kind == BatteryOperation.UpgradeModule)
        {
            if (OrderedModule(o)?.TryAdvanceTo(o.TargetLevel) != true) return false;
        }
        else CapacityLevel++;
        foreach (var cost in o.Costs)
        {
            int left = cost.count;
            for (int i = Contents.Count - 1; i >= 0 && left > 0; i--)
            {
                var item = Contents[i];
                if (item.def != cost.thingDef || !IsStaged(item)) continue;
                int take = Math.Min(item.stackCount, left);
                item.SplitOff(take).Destroy();
                left -= take;
            }
        }
        WorkGiver_ModularBatteryAssembly.SetPartClaim(Map, o, false);
        Orders.RemoveAt(0);
        blockedReturnOrderId = 0;
        RefreshMarketValue();
        RefreshBatteryPower();
        UpdateWorkListing();
        return true;
    }
    public bool RetryAssemblyReturn()
    {
        var o = PendingOrder;
        if (!AssemblyBlocked || o.Kind != BatteryOperation.Remove || o.WorkLeft != 0f || !Spawned
            || Faction != Faction.OfPlayer || this.IsBurning() || !OrderValid(o)) return false;
        blockedReturnOrderId = 0;
        DoWork(1f, o.Id);
        return PendingOrder?.Id != o.Id;
    }
    public bool CancelOrder(int orderId)
    {
        if (!Spawned) return false;
        int index = 0;
        while (index < Orders.Count && Orders[index].Id != orderId) index++;
        if (index == Orders.Count) return false;
        if (index == 0 && contents != null)
            for (int i = contents.Count - 1; i >= 0; i--)
                if (IsStaged(contents[i]) && !contents.TryDrop(contents[i], Position, Map, ThingPlaceMode.Near, out _)) return false;
        WorkGiver_ModularBatteryAssembly.SetPartClaim(Map, Orders[index], false);
        Orders.RemoveAt(index);
        if (blockedReturnOrderId == orderId) blockedReturnOrderId = 0;
        if (index == 0) UpdateWorkListing();
        return true;
    }
    public override void Notify_MinifiedThingAboutToBeDestroyed(DestroyMode mode)
    {
        base.Notify_MinifiedThingAboutToBeDestroyed(mode);
        if (mode is DestroyMode.Deconstruct or DestroyMode.KillFinalize && MapHeld != null) ReturnHeldContents(MapHeld, PositionHeld);
    }
    private bool ReturnHeldContents(Map map, IntVec3 cell)
    {
        bool returned = Contents.TryDropAll(cell, map, ThingPlaceMode.Near);
        if (leftModule != null && !Contents.Contains(leftModule)) SetModule("left", null);
        if (rightModule != null && !Contents.Contains(rightModule)) SetModule("right", null);
        if (!Destroyed) RefreshBatteryPower();
        return returned;
    }
    public override void Destroy(DestroyMode mode = DestroyMode.Vanish)
    {
        if (Destroyed) return;
        var map = MapHeld; var cell = PositionHeld;
        if (mode == DestroyMode.Deconstruct && Spawned && !ReturnHeldContents(map, cell))
        {
            Messages.Message(L.T("MT_NoDropSpace"), this, MessageTypeDefOf.RejectInput);
            return;
        }
        base.Destroy(mode);
        if (!Destroyed) return;
        if (mode == DestroyMode.KillFinalize && map != null) ReturnHeldContents(map, cell);
        Contents.ClearAndDestroyContents();
    }
}
