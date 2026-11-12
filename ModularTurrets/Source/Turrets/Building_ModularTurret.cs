using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using RimWorld;
using UnityEngine;
using Verse;

namespace ModularTurrets;

// This object owns saved parts, orders and channel state. The feature partials
// share that ownership; they must not keep a second copy of installed items.
[StaticConstructorOnStartup]
public sealed partial class Building_ModularTurret : Building_Turret, IThingHolder
{
    private ThingOwner<Thing> contents;
    private List<InstalledMount> fitted = new();
    public List<TurretOrder> Orders = new();
    // Kept only for old saves and external fixtures; manual pausing no longer gates work.
    public bool OrdersPaused, HoldFire;
    private int blockedReturnOrderId;
    public bool AssemblyBlocked => blockedReturnOrderId != 0 && PendingOrder?.Id == blockedReturnOrderId;
    public bool AutoReload = true;
    public int DamageLevel, ArmorLevel, RangeLevel, Shells;
    private int nextOrderId = 1;
    private float shieldEnergy, aimAngle;
    private int shieldDelay;
    public Thing HeadPart => PartAt("head");
    public PartExtension Head => HeadPart?.def.GetModExtension<PartExtension>();
    public override string LabelNoCount
    {
        get
        {
            var headDef = fitted?.FirstOrDefault(p => p?.Slot == "head")?.Part?.def;
            if (headDef == null) return base.LabelNoCount;
            string key = headDef.defName switch
            {
                "MT_Head_Autocannon" => "MT_TurretNameAutocannon",
                "MT_Head_Sniper" => "MT_TurretNameSniper",
                "MT_Head_DualAutocannon" => "MT_TurretNameDualAutocannon",
                "MT_Head_DualSniper" => "MT_TurretNameDualSniper",
                "MT_Head_DualMortar" => "MT_TurretNameDualMortar",
                "MT_Head_Flamethrower" => "MT_TurretNameFlamethrower",
                "MT_Head_Minigun" => "MT_TurretNameMinigun",
                "MT_Head_Laser" => "MT_TurretNameLaser",
                "MT_Head_Missile" => "MT_TurretNameMissile",
                "MT_Head_Tesla" => "MT_TurretNameTesla",
                "MT_Head_Quantum" => "MT_TurretNameQuantum",
                _ => null
            };
            string label = key == null ? L.T("MT_TurretNameGeneric", L.Label(headDef)) : L.T(key);
            // Preserve the standard ThingWithComps label transformations, including
            // any custom-name component supplied by another mod.
            foreach (var comp in AllComps) label = comp.TransformLabel(label);
            return label;
        }
    }
    public float AimAngle => aimAngle;
    public bool ShieldVisible => Powered && shieldEnergy > 0f;
    public TurretOrder PendingOrder => Orders.FirstOrDefault();
    private ThingOwner<Thing> Contents => contents ??= new ThingOwner<Thing>(this);
    public Thing PartAt(string slot)
    {
        foreach (var mount in CollectionsMarshal.AsSpan(fitted))
            if (mount.Slot == slot) return mount.Part;
        return null;
    }
    public bool CanMountPart(string slot, ThingDef partDef)
    {
        var part = partDef?.GetModExtension<PartExtension>();
        if (part == null || (slot == "head" ? !part.isHead : part.isHead || Head?.mounts?.Any(m => m.id == slot) != true)) return false;
        var specialist = partDef.GetModExtension<SpecialistModuleExtension>();
        return specialist == null || slot != "head" && HeadPart != null
            && specialist.compatibleHeads?.Contains(HeadPart.def) == true
            && specialist.allowedSlots?.Contains(slot) == true
            && part.anchors?.Any(a => a.slot == slot && a.graphicData != null) == true;
    }
    public static bool CanUpgradePart(ThingDef partDef)
        => partDef?.GetModExtension<PartExtension>() is { canUpgrade: true }
            && partDef.GetModExtension<SpecialistModuleExtension>() == null;
    public bool HasSpecialistInstallConflict(ThingDef partDef, TurretOrder currentOrder = null)
    {
        if (partDef?.GetModExtension<SpecialistModuleExtension>() == null) return false;
        // A queued removal still owns an installed module until removal succeeds.
        if (fitted.Any(m => m.Part?.def == partDef)) return true;
        // New requests consider every reservation. In an old save containing
        // duplicate install orders, let the first one finish instead of making
        // each order wait forever for the other. Never remove either saved item.
        var reservations = currentOrder != null && Orders.Contains(currentOrder)
            ? Orders.TakeWhile(o => !ReferenceEquals(o, currentOrder)) : Orders;
        return reservations.Any(o => !ReferenceEquals(o, currentOrder) && o.Kind == TurretOperation.Install && o.PartDef == partDef);
    }
    private IEnumerable<Thing> Staged => EnumerateStaged(Contents);
    private IEnumerable<Thing> EnumerateStaged(ThingOwner<Thing> source)
    {
        foreach (Thing thing in source)
            if (!IsFitted(thing)) yield return thing;
    }
    private bool IsFitted(Thing thing)
    {
        foreach (var mount in fitted)
            if (mount.Part == thing) return true;
        return false;
    }
    private bool HasStagedInstallPart()
    {
        foreach (Thing thing in Contents)
            if (!IsFitted(thing) && Wants(thing)) return true;
        return false;
    }
    public bool Powered => !IsWrecked && Spawned && (selfPowered ? InternalSupplyAvailable
        : GetComp<CompPowerTrader>()?.PowerOn != false && GetComp<CompFlickable>()?.SwitchIsOn != false);
    public bool CanReceive => Spawned && Faction == Faction.OfPlayer && !AssemblyBlocked && !this.IsBurning() && PendingOrder != null && OrderValid(PendingOrder);
    public bool CanWork
    {
        get
        {
            if (!CanReceive) return false;
            var order = PendingOrder;
            if (order.Kind == TurretOperation.Reload) return false;
            // Keep stock checks live: delivery/removal can happen between calls
            // in one tick. Avoid allocating predicates for each material/stack.
            foreach (var cost in order.Costs)
                if (Needed(cost.thingDef) != 0) return false;
            return order.Kind != TurretOperation.Install || HasStagedInstallPart();
        }
    }
    public override bool IsEverThreat => !IsWrecked && HeadPart != null;
    public ThingOwner GetDirectlyHeldThings() => Contents;
    public void GetChildHolders(List<IThingHolder> outChildren)
    {
        ThingOwnerUtility.AppendThingHoldersFromThings(outChildren, Contents);
        if (deferredWreckMaterials != null) outChildren.Add(deferredWreckMaterials);
    }

    public override void ExposeData()
    {
        // Restore the Thing ID before registering held-item cross references.
        base.ExposeData();
        ExposeUpgradeInvestment();
        ExposeWreckState();
        ExposeDurability();
        Scribe_Collections.Look(ref muzzleIndices, "modularMuzzleIndices", LookMode.Value, LookMode.Value);
        Scribe_Deep.Look(ref contents, "modularContents", this);
        Scribe_Collections.Look(ref fitted, "fitted", LookMode.Deep);
        Scribe_Collections.Look(ref Orders, "assemblyOrders", LookMode.Deep);
        Scribe_Collections.Look(ref channels, "fireChannels", LookMode.Deep);
        Scribe_Values.Look(ref OrdersPaused, "ordersPaused");
        Scribe_Values.Look(ref HoldFire, "holdFire");
        Scribe_Values.Look(ref blockedReturnOrderId, "assemblyBlockedReturnOrderId");
        Scribe_Values.Look(ref AutoReload, "autoReload", true);
        Scribe_Values.Look(ref DamageLevel, "damageLevel");
        Scribe_Values.Look(ref ArmorLevel, "armorLevel");
        Scribe_Values.Look(ref RangeLevel, "rangeLevel");
        Scribe_Values.Look(ref Shells, "shells");
        Scribe_Values.Look(ref nextOrderId, "nextOrderId", 1);
        ExposeMortarAmmunition();
        Scribe_Values.Look(ref shieldEnergy, "shieldEnergy");
        Scribe_Values.Look(ref shieldDelay, "shieldDelay");
        Scribe_Values.Look(ref aimAngle, "aimAngle");
        Scribe_Values.Look(ref missileLane, "modularMissileLane", 0);
        if (Scribe.mode == LoadSaveMode.PostLoadInit)
        {
            // References are resolved now. Rebuild derived combat data only here,
            // keeping the saved gun, order and in-progress firing state intact.
            fitted ??= new(); Orders ??= new(); channels ??= new();
            fitted.RemoveAll(p => p.Part == null || !Contents.Contains(p.Part)); Orders.RemoveAll(o => o == null);
            if (!Orders.Any(o => o.Kind == TurretOperation.Restore)) RestoreDeferredWreckMaterials();
            TrimUnstartedModuleUpgrades();
            if (OrdersPaused && blockedReturnOrderId == 0)
                blockedReturnOrderId = Orders.FirstOrDefault(o => o.Kind == TurretOperation.Remove && o.RemovalRolled && o.WorkLeft <= 0f)?.Id ?? 0;
            OrdersPaused = false;
            if (!Orders.Any(o => o.Id == blockedReturnOrderId && o.Kind == TurretOperation.Remove && o.RemovalRolled && o.WorkLeft <= 0f))
                blockedReturnOrderId = 0;
            DamageLevel = Math.Max(0, DamageLevel); ArmorLevel = Math.Max(0, ArmorLevel); RangeLevel = Math.Max(0, RangeLevel); Shells = Math.Clamp(Shells, 0, 20);
            nextOrderId = Math.Max(nextOrderId, Orders.Select(o => o.Id).DefaultIfEmpty(0).Max() + 1);
            LoadReloadShells();
            RebuildChannels();
            if (IsWrecked) StopWreckedSystems();
        }
    }
    public override void SpawnSetup(Map map, bool respawningAfterLoad)
    {
        base.SpawnSetup(map, respawningAfterLoad);
        if (!respawningAfterLoad) aimAngle = Rotation.AsAngle;
        LoadReloadShells();
        RebuildChannels();
        if (IsWrecked) StopWreckedSystems();
    }
    public int Needed(ThingDef def)
    {
        var o = PendingOrder;
        if (o == null || def == null) return 0;
        if (o.Kind == TurretOperation.Install && o.PartDef == def) return HasStagedInstallPart() ? 0 : 1;
        int loaded = o.Kind == TurretOperation.Reload && def == ReloadShellType(o) ? o.ReloadedShells : 0;
        int required = 0, staged = 0;
        foreach (var cost in o.Costs)
            if (cost.thingDef == def) required = checked(required + cost.count);
        foreach (Thing thing in Contents)
            if (thing.def == def && !IsFitted(thing)) staged = checked(staged + thing.stackCount);
        return Math.Max(0, required - loaded - staged);
    }
    public bool Wants(Thing t)
    {
        var o = PendingOrder;
        if (o == null || t == null) return false;
        if (o.Kind == TurretOperation.Install) return t.def == o.PartDef && (o.PartId == 0 || t.thingIDNumber == o.PartId);
        foreach (var cost in o.Costs)
            if (cost.thingDef == t.def && Needed(t.def) > 0) return true;
        return false;
    }
    public int Receive(Thing carried, int orderId)
    {
        // A queued job can outlive a cancelled order. Never deliver into its replacement.
        if (!CanReceive || PendingOrder.Id != orderId || !Wants(carried)) return 0;
        int count = PendingOrder.Kind == TurretOperation.Install ? 1 : Math.Min(carried.stackCount, Needed(carried.def));
        int received = count > 0 ? Contents.TryAddOrTransfer(carried, count) : 0;
        if (received > 0) LoadReloadShells();
        return received;
    }
    private void LoadReloadShells()
    {
        var o = PendingOrder;
        if (o?.Kind != TurretOperation.Reload || IsWrecked) return;
        ThingDef shellDef = ReloadShellType(o);
        if (shellDef == null || Shells > 0 && CoreLoadedMortarAmmo != shellDef) return;
        o.WorkTotal = o.WorkLeft = 0f;
        int left = Math.Min(Math.Max(0, 20 - Shells), Math.Max(0, ReloadShellCount(o) - o.ReloadedShells));
        if (left > 0) foreach (var t in Staged.Where(t => t.def == shellDef).ToList())
        {
            int take = Math.Min(t.stackCount, left);
            t.SplitOff(take).Destroy();
            LoadedShellDef = shellDef;
            Shells += take; o.ReloadedShells += take; left -= take;
            if (left == 0) break;
        }
        if (o.ReloadedShells >= ReloadShellCount(o) && o.Costs.All(c => Needed(c.thingDef) == 0)) FinishOrder(o);
    }
    private static int ReloadShellCount(TurretOrder o)
        => o.Costs.Where(c => c.thingDef == ReloadShellType(o)).Sum(c => c.count);
    public bool WorkReady(int orderId) => PendingOrder?.Id == orderId && CanWork;
    private bool OrderValid(TurretOrder o)
    {
        if (IsWrecked && o.Kind != TurretOperation.Restore) return false;
        switch (o.Kind)
        {
            case TurretOperation.Restore: return IsWrecked;
            case TurretOperation.Install:
                return PartAt(o.Slot) == null && CanMountPart(o.Slot, o.PartDef) && !HasSpecialistInstallConflict(o.PartDef, o);
            case TurretOperation.Remove: return PartAt(o.Slot)?.thingIDNumber == o.PartId && (o.Slot != "head" || fitted.All(p => p.Slot == "head"));
            case TurretOperation.UpgradeModule:
                var part = PartAt(o.Slot);
                var comp = part?.TryGetComp<CompTurretPart>();
                if (part?.thingIDNumber != o.PartId || comp == null || comp.IsParagon || !CanUpgradePart(part.def)) return false;
                return o.TargetParagon
                    ? o.TargetTier == 5 && comp.Tier == 5 && ParagonUtility.IsEligible(part.def)
                        && ResearchDone(ParagonUtility.Settings(part.def).research) && ResearchDone("MT_ExoticTurrets")
                        && !ParagonMapRegistry.HasUpgradeConflict(this, part, o)
                    : o.TargetTier >= 1 && o.TargetTier <= 5 && comp.Tier == o.TargetTier - 1;
            case TurretOperation.Reload: return ReloadShellType(o) is { } shell
                && (Shells == 0 || CoreLoadedMortarAmmo == shell)
                && (Shells < 20 || o.ReloadedShells >= ReloadShellCount(o));
            default: return Level(o.Kind) == o.TargetTier - 1;
        }
    }
    public void DoWork(float amount, int orderId)
    {
        if (!float.IsFinite(amount) || amount <= 0 || !WorkReady(orderId)) return;
        var o = PendingOrder;
        o.WorkLeft = Math.Max(0f, o.WorkLeft - amount);
        if (o.WorkLeft > 0f) return;
        RecordCompletedUpgradeInvestment(o);
        if (o.Kind == TurretOperation.Install) fitted.Add(new InstalledMount { Slot = o.Slot, Part = Staged.First(Wants) });
        else if (o.Kind == TurretOperation.Remove)
        {
            var mount = MountForRemoval(o.Slot);
            // Roll once at completion, retaining the outcome when placement needs retrying.
            if (!o.RemovalRolled) { o.RemovalRolled = true; o.RemovalFailed = Rand.Chance(0.01f); }
            if (o.RemovalFailed)
            {
                var scrap = ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("ChunkSlagSteel"));
                if (!GenPlace.TryPlaceThing(scrap, Position, Map, ThingPlaceMode.Near))
                {
                    // Slag is a single non-stacking item. A failed placement leaves
                    // this temporary item unspawned; the installed part stays owned.
                    if (!scrap.Spawned && !scrap.Destroyed) scrap.Destroy();
                    blockedReturnOrderId = o.Id;
                    return;
                }
                mount.Part.Destroy(); Messages.Message(L.T("MT_RemovalFailure"), this, MessageTypeDefOf.NegativeEvent);
            }
            else if (!Contents.TryDrop(mount.Part, Position, Map, ThingPlaceMode.Near, out _)) { blockedReturnOrderId = o.Id; return; }
            fitted.Remove(mount);
        }
        else if (o.Kind == TurretOperation.UpgradeModule)
        {
            var comp = PartAt(o.Slot).TryGetComp<CompTurretPart>();
            if (o.TargetParagon) comp.IsParagon = true;
            else comp.Tier = o.TargetTier;
        }
        else if (o.Kind == TurretOperation.Damage) DamageLevel++;
        else if (o.Kind == TurretOperation.Armor) ArmorLevel++;
        else if (o.Kind == TurretOperation.Range) RangeLevel++;
        else if (o.Kind == TurretOperation.Restore) CompleteWreckRepair();
        FinishOrder(o);
    }
    // Keep the captured slot in the rare completion path. A lambda capturing
    // the current order here would allocate a display class on every work tick.
    private InstalledMount MountForRemoval(string slot) => fitted.First(mount => mount.Slot == slot);
    private void FinishOrder(TurretOrder o)
    {
        foreach (var cost in o.Costs)
        {
            if (o.Kind == TurretOperation.Reload && cost.thingDef == ReloadShellType(o)) continue;
            int left = cost.count;
            foreach (var t in Staged.Where(t => t.def == cost.thingDef).ToList())
            {
                int take = Math.Min(t.stackCount, left); t.SplitOff(take).Destroy(); left -= take;
                if (left == 0) break;
            }
        }
        Orders.RemoveAt(0);
        if (o.Kind == TurretOperation.Restore) RestoreDeferredWreckMaterials();
        LoadReloadShells();
        if (o.Kind is TurretOperation.Install or TurretOperation.Remove or TurretOperation.UpgradeModule) ParagonMapRegistry.Dirty(Map);
        if (blockedReturnOrderId == o.Id) blockedReturnOrderId = 0;
        RebuildChannels();
        RefreshMarketValue();
    }
    public bool RetryAssemblyReturn()
    {
        var o = PendingOrder;
        if (!AssemblyBlocked || o.Kind != TurretOperation.Remove || !o.RemovalRolled || o.WorkLeft > 0f
            || !Spawned || Faction != Faction.OfPlayer || this.IsBurning() || !OrderValid(o)) return false;
        blockedReturnOrderId = 0;
        try { DoWork(1f, o.Id); }
        finally
        {
            // A failed retry (including missing staged costs) must not silently
            // reopen the queue. The existing completed order owns its saved roll.
            if (Orders.Any(order => order.Id == o.Id)) blockedReturnOrderId = o.Id;
        }
        return !Orders.Any(order => order.Id == o.Id);
    }
    public bool CanCancelOrder(int index)
        => index >= 0 && index < Orders.Count && !(Orders[index].Kind == TurretOperation.Remove && Orders[index].RemovalRolled)
            && (PendingOrder?.Kind != TurretOperation.Restore || index == 0);
    public bool CancelOrder(int index)
    {
        if (!CanCancelOrder(index)) return false;
        var canceled = Orders[index];
        bool baseUpgrade = IsBaseUpgrade(canceled.Kind);
        // Reload can temporarily sit in front of the only engineering order
        // that has received materials. Keep its shells when canceling that order.
        var returns = canceled.Kind == TurretOperation.Restore ? Staged.Where(t => canceled.Costs.Any(c => c.thingDef == t.def))
            : index == 0 ? Staged
            : baseUpgrade && index == Orders.FindIndex(o => o.Kind != TurretOperation.Reload)
                ? Staged.Where(t => canceled.Costs.Any(c => c.thingDef == t.def))
                : Enumerable.Empty<Thing>();
        foreach (var t in returns.ToList()) if (!Contents.TryDrop(t, Position, Map, ThingPlaceMode.Near, out _)) return false;
        if (baseUpgrade)
        {
            Orders.RemoveAt(index);
            int level = Level(canceled.Kind);
            for (int i = 0; i < Orders.Count; i++)
            {
                var order = Orders[i];
                if (order.Kind != canceled.Kind) continue;
                if (i >= index) SetBaseUpgradeTerms(order, level);
                level++;
            }
        }
        else Orders.RemoveAll(o => o.Id == canceled.Id || o.Id > canceled.Id && o.Kind == canceled.Kind && o.Slot == canceled.Slot);
        if (canceled.Kind == TurretOperation.Restore) RestoreDeferredWreckMaterials();
        if (canceled.Kind == TurretOperation.UpgradeModule) ParagonMapRegistry.Dirty(Map);
        if (!Orders.Any(o => o.Id == blockedReturnOrderId)) blockedReturnOrderId = 0;
        return true;
    }
    private static bool IsBaseUpgrade(TurretOperation kind)
        => kind is TurretOperation.Damage or TurretOperation.Armor or TurretOperation.Range;
    private static bool BaseUpgradeHasEffect(TurretOperation kind, int level)
        => kind != TurretOperation.Armor || ArmorDamageMultiplier(level + 1) < ArmorDamageMultiplier(level);
    private readonly record struct BaseUpgradeQuote(ThingDef Primary, int PrimaryCount,
        ThingDef Secondary, int SecondaryCount, ThingDef Components, int ComponentCount, float Work);

    private static BaseUpgradeQuote NewBaseUpgradeQuote(int level)
    {
        float root = MathF.Sqrt(Math.Max(0, level));
        return new(ThingDefOf.Steel, 60 + (int)(8f * root), ThingDefOf.Plasteel, 30 + (int)(4f * root),
            ThingDefOf.ComponentIndustrial, 3 + (int)(root / 4f), 5000f * (1f + 0.06f * root));
    }

    private static void SetBaseUpgradeTerms(TurretOrder order, int level, bool newQuote = false)
    {
        // Reindexing a saved queue keeps that order's component price schedule.
        bool currentComponents = newQuote || order.Costs.Any(c => c?.thingDef?.defName == "ComponentIndustrial");
        bool ordinaryMaterials = newQuote || order.Costs.Any(c => c?.thingDef?.defName == "Plasteel")
            && !order.Costs.Any(c => c?.thingDef?.defName == "Uranium");
        float completed = Math.Max(0f, order.WorkTotal - order.WorkLeft);
        var quote = NewBaseUpgradeQuote(level);
        order.TargetTier = level + 1;
        order.WorkTotal = quote.Work;
        order.WorkLeft = Math.Max(0f, order.WorkTotal - completed);
        order.Costs = new() { new(quote.Primary, quote.PrimaryCount),
            new(ordinaryMaterials ? quote.Secondary : DefDatabase<ThingDef>.GetNamed("Uranium"), quote.SecondaryCount),
            new(ordinaryMaterials ? quote.Components : DefDatabase<ThingDef>.GetNamed("ComponentSpacer"), quote.ComponentCount) };
        if (currentComponents && !ordinaryMaterials) ComponentEconomy.ApplyToNewQuote(order.Costs);
    }
    private int Level(TurretOperation kind) => kind == TurretOperation.Damage ? DamageLevel : kind == TurretOperation.Armor ? ArmorLevel : RangeLevel;
    private static bool ResearchDone(string name) => !string.IsNullOrEmpty(name)
        && DefDatabase<ResearchProjectDef>.GetNamedSilentFail(name)?.IsFinished == true;
    private bool AddOrder(TurretOrder o)
    {
        if (HasPendingRelocation)
        {
            Messages.Message(L.T("MT_RelocateOrderBlocked"), this, MessageTypeDefOf.RejectInput, false);
            return false;
        }
        if (!Spawned || Faction != Faction.OfPlayer || Orders.Count >= 100 && o.Kind != TurretOperation.Restore
            || nextOrderId == int.MaxValue || IsWrecked && o.Kind != TurretOperation.Restore) return false;
        o.Id = nextOrderId++; o.WorkLeft = o.WorkTotal; Orders.Add(o);
        if (o.TargetParagon) ParagonMapRegistry.Dirty(Map);
        return true;
    }
    public bool QueueInstall(string slot, Thing part)
    {
        if (part == null || !part.Spawned || part.Map != Map || PartAt(slot) != null
            || Orders.Any(o => o.Slot == slot || o.Kind == TurretOperation.Install && o.PartId == part.thingIDNumber)) return false;
        var o = new TurretOrder { Kind = TurretOperation.Install, Slot = slot, PartDef = part.def, PartId = part.thingIDNumber, WorkTotal = 100f };
        return OrderValid(o) && AddOrder(o);
    }
    public bool QueueRemove(string slot)
    {
        var part = PartAt(slot);
        if (part == null || Orders.Any(o => o.Slot == slot)) return false;
        var o = new TurretOrder { Kind = TurretOperation.Remove, Slot = slot, PartId = part.thingIDNumber, WorkTotal = 100f };
        return OrderValid(o) && AddOrder(o);
    }
    public bool QueueModuleUpgrade(string slot)
    {
        var part = PartAt(slot);
        if (part == null || !CanUpgradePart(part.def) || Orders.Any(o => o.Slot == slot)) return false;
        var comp = part.TryGetComp<CompTurretPart>();
        if (comp == null || comp.IsParagon) return false;
        if (comp.Tier == 5)
        {
            // Inventory transfers performed by other mods may not invoke spawn
            // hooks. A user request needs a fresh map snapshot before reserving.
            ParagonMapRegistry.Dirty(MapHeld);
            if (!ParagonUtility.IsEligible(part.def) || ParagonMapRegistry.HasUpgradeConflict(this, part)) return false;
            var settings = ParagonUtility.Settings(part.def);
            if (!ResearchDone(settings.research) || !ResearchDone("MT_ExoticTurrets")
                || !float.IsFinite(settings.work) || settings.work <= 0f || settings.costs == null || settings.costs.Count == 0
                || settings.costs.Any(c => c?.thingDef == null || c.count <= 0)) return false;
            var order = new TurretOrder { Kind = TurretOperation.UpgradeModule, Slot = slot, PartId = part.thingIDNumber,
                TargetTier = 5, TargetParagon = true, WorkTotal = settings.work,
                Costs = ModuleUpgradeCosts.ForUpgrade(part.def, 5, true) };
            return OrderValid(order) && AddOrder(order);
        }
        int tier = comp.Tier + 1;
        if (tier < 1 || tier > 5 || !ResearchDone(tier >= 4 ? "MT_ExoticTurrets" : "MT_AdvancedTurrets")) return false;
        return AddOrder(new TurretOrder { Kind = TurretOperation.UpgradeModule, Slot = slot, PartId = part.thingIDNumber, TargetTier = tier,
            WorkTotal = 6000f * tier * tier * tier, Costs = ModuleUpgradeCosts.ForUpgrade(part.def, tier) });
    }
    private void TrimUnstartedModuleUpgrades()
    {
        foreach (var group in Orders.Where(o => o.Kind == TurretOperation.UpgradeModule).GroupBy(o => (o.Slot, o.PartId)).ToList())
        {
            var part = PartAt(group.Key.Slot);
            var comp = part?.TryGetComp<CompTurretPart>();
            if (part?.thingIDNumber != group.Key.PartId || comp == null) continue;
            var first = group.FirstOrDefault(o => !comp.IsParagon && (o.TargetParagon
                ? comp.Tier == 5 && o.TargetTier == 5 && ParagonUtility.IsEligible(part.def)
                : o.TargetTier == comp.Tier + 1)) ?? group.First();
            bool afterFirst = false;
            foreach (var order in group)
            {
                if (ReferenceEquals(order, first)) { afterFirst = true; continue; }
                // Only discard untouched future reservations. Unusual saved work
                // or invalid totals stay intact rather than losing invested work.
                if (afterFirst && float.IsFinite(order.WorkTotal) && order.WorkTotal > 0f
                    && float.IsFinite(order.WorkLeft) && order.WorkLeft == order.WorkTotal)
                {
                    Orders.Remove(order);
                    if (order.TargetParagon) ParagonMapRegistry.Dirty(Map);
                }
            }
        }
    }
    public bool QueueBaseUpgrade(TurretOperation kind)
    {
        if (!IsBaseUpgrade(kind) || !ResearchDone("MT_InfiniteEngineering")) return false;
        int n = Level(kind) + Orders.Count(o => o.Kind == kind);
        if (n >= int.MaxValue - 100) return false;
        if (!BaseUpgradeHasEffect(kind, n)) return false;
        var order = new TurretOrder { Kind = kind };
        SetBaseUpgradeTerms(order, n, newQuote: true);
        return AddOrder(order);
    }
    public bool QueueReload()
    {
        if (!CombatCompatibility.IsAvailable || CombatCompatibility.Provider != null) return false;
        if (Head?.usesShells != true || Shells >= 20 || Orders.Any(o => o.Kind == TurretOperation.Reload)) return false;
        ThingDef shell = CoreSelectedMortarAmmo;
        if (!MortarShellProjectiles.Supports(shell) || Shells > 0 && CoreLoadedMortarAmmo != shell) return false;
        if (!AddOrder(new TurretOrder { Kind = TurretOperation.Reload, Costs = new() { new ThingDefCountClass(shell, 20 - Shells) } })) return false;
        // Reload preempts engineering while retaining its work and delivered materials.
        var reload = Orders[Orders.Count - 1]; Orders.RemoveAt(Orders.Count - 1); Orders.Insert(0, reload);
        return true;
    }
    private static ThingDefCountClass Cost(string name, int count) => new(DefDatabase<ThingDef>.GetNamed(name), count);
    public bool ConsumeShell() { if (Shells <= 0) return false; if (--Shells == 0) LoadedShellDef = null; return true; }
    public override void Destroy(DestroyMode mode = DestroyMode.Vanish)
    {
        if (mode == DestroyMode.KillFinalize && TryEnterWreckedState()) return;
        Map explosionMap = null;
        IntVec3 explosionCell = Position;
        int explosionFloor = 0;
        // Despawn removes floor ownership. Capture it only for a final combat
        // destruction, so moving, deconstructing and entering wreck state are safe.
        if (mode == DestroyMode.KillFinalize && Spawned && !Destroyed && TryGetFloor(this, out explosionFloor))
            explosionMap = Map;
        if (Spawned && !IsWrecked && (mode == DestroyMode.Deconstruct
            || mode == DestroyMode.KillFinalize && fitted.Count == 0))
        {
            if (!TryGetFloor(this, out int recoveryFloor)) return;
            RestoreDeferredWreckMaterials();
            if (!MaterializeMortarStock()) return;
            // A held part may retain its old floor membership. Pin the current
            // base floor before removing the holder that supplies that identity.
            foreach (var thing in Contents)
                if (!FloorCompatibility.TrySetFloor(thing, recoveryFloor)) return;
            // Like per-slot removal, each attached part independently has a 1%
            // scrap outcome. Stage ammunition first so a blocked unload never
            // re-rolls parts before a later deconstruction attempt.
            foreach (var mount in fitted.ToList()) if (Rand.Chance(0.01f))
            {
                var scrap = ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("ChunkSlagSteel"));
                // A rejected replacement keeps the original item; the risk must
                // never turn a failed storage hand-off into silent item loss.
                if (FloorCompatibility.TrySetFloor(scrap, recoveryFloor) && Contents.TryAdd(scrap)) mount.Part.Destroy();
                else if (!scrap.Spawned && !scrap.Destroyed) scrap.Destroy();
            }
            var map = Map; var cell = Position;
            var recovery = new ThingOwner<Thing>();
            foreach (var thing in Contents.ToList()) { Contents.Remove(thing); recovery.TryAdd(thing); }
            base.Destroy(mode);
            recovery.TryDropAll(cell, map, ThingPlaceMode.Near);
            // Removing the base has freed this footprint; keep even overfull-map outputs.
            foreach (var thing in recovery.ToList()) { recovery.Remove(thing); GenSpawn.Spawn(thing, cell, map); }
            ExplodeAfterDestruction(explosionMap, explosionCell, explosionFloor);
            return;
        }
        deferredWreckMaterials?.Contents.ClearAndDestroyContents();
        Contents.ClearAndDestroyContents();
        // Final destruction of a wreck returns no base-cost slag or recoverable parts.
        base.Destroy(IsWrecked && mode is DestroyMode.KillFinalize or DestroyMode.Deconstruct ? DestroyMode.Vanish : mode);
        ExplodeAfterDestruction(explosionMap, explosionCell, explosionFloor);
    }
    internal static bool TryGetFloor(Thing thing, out int floor)
        => FloorCompatibility.TryGetFloor(thing, out floor);
    public static bool SameFloor(Thing a, Thing b)
    {
        if (a == null || b == null || a.Map != b.Map) return false;
        return TryGetFloor(a, out int aFloor) && TryGetFloor(b, out int bFloor) && aFloor == bFloor;
    }
}
