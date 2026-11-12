using System;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace ModularTurrets;

// A map owns the first observed paragon of each ordinary module kind. Duplicate
// imports retain their upgrade history, but use tier-V effects until succession.
public sealed class ParagonMapRegistry : MapComponent
{
    private static ThingDef[] relatedDefs;
    private static int relatedDefCount = -1;
    private static ThingDef[] RelatedDefs
    {
        get
        {
            var defs = DefDatabase<ThingDef>.AllDefsListForReading;
            if (relatedDefs == null || relatedDefCount != defs.Count)
            {
                // Built after Def loading, including external heads/modules.
                // Rebuild if a runtime-generated definition is added or removed.
                relatedDefs = defs.Where(def => def.thingClass != null
                    && (typeof(Building_ModularTurret).IsAssignableFrom(def.thingClass)
                        || def.comps?.Any(comp => comp.compClass == typeof(CompTurretPart)) == true)).ToArray();
                relatedDefCount = defs.Count;
            }
            return relatedDefs;
        }
    }

    private Dictionary<string, Thing> winners = new();
    private Dictionary<Thing, int> arrivalOrder = new();
    private int nextArrival = 1;
    private List<string> winnerKeys;
    private List<Thing> winnerValues, arrivalKeys;
    private List<int> arrivalValues;
    private Dictionary<string, List<Thing>> present = new();
    private HashSet<Thing> effective = new();
    private List<Building_ModularTurret> turrets = new();
    private bool dirty = true, scanning, refreshing;
    private bool pendingDeparture, dormant;
    private MapEvents subscribedEvents;
    private int lastScanTick = -1;

    // Keep published caches intact during holder callbacks. The alternate cache
    // is swapped in only after a complete scan; traversal scratch is never exposed.
    private Dictionary<string, List<Thing>> scanPresent = new();
    private Dictionary<string, Thing> scanWinners = new();
    private HashSet<Thing> scanEffective = new();
    private List<Building_ModularTurret> scanTurrets = new();
    private readonly Stack<List<Thing>> scanPartLists = new();
    private readonly HashSet<Thing> scanFoundParts = new(), scanSeenThings = new(), scanChanged = new();
    private readonly HashSet<IThingHolder> scanSeenHolders = new();
    private readonly Queue<IThingHolder> scanPending = new();
    private readonly List<IThingHolder> scanChildren = new();
    private readonly List<Thing> scanDepartures = new();
    private readonly List<Building_ModularTurret> scanRefreshCandidates = new();

    private List<Thing> ScanPartList()
        => scanPartLists.Count == 0 ? new List<Thing>() : scanPartLists.Pop();

    private void ClearScanScratch()
    {
        foreach (List<Thing> list in scanPresent.Values) { list.Clear(); scanPartLists.Push(list); }
        scanPresent.Clear(); scanWinners.Clear(); scanEffective.Clear(); scanTurrets.Clear();
        scanFoundParts.Clear(); scanSeenThings.Clear(); scanSeenHolders.Clear();
        scanPending.Clear(); scanChildren.Clear(); scanDepartures.Clear();
    }

    private static bool HoldsChangedPart(ThingOwner things, HashSet<Thing> changed)
    {
        foreach (Thing part in things)
            if (changed.Contains(part)) return true;
        return false;
    }

    public ParagonMapRegistry(Map map) : base(map) { }

    public static void Dirty(Map map)
    {
        // Spawn/holder events wake an empty map. Do not replace this with polling:
        // the no-turret case must remain dormant between actual arrivals.
        if (map?.GetComponent<ParagonMapRegistry>() is { } registry)
        {
            registry.dirty = true;
            registry.dormant = false;
        }
    }

    private static bool IsRelated(Thing thing)
        => thing is Building_ModularTurret || thing.TryGetComp<CompTurretPart>() != null;

    private static bool HasHolders(Thing thing)
        => thing is IThingHolder || thing is ThingWithComps withComps
            && withComps.AllComps?.Any(c => c is IThingHolder) == true;

    private static bool ContainsRelated(Thing thing)
    {
        if (IsRelated(thing)) return true;
        if (thing is IThingHolder holder)
            foreach (Thing item in ThingOwnerUtility.GetAllThingsRecursively(holder))
                if (IsRelated(item)) return true;
        if (thing is ThingWithComps withComps && withComps.AllComps != null)
            foreach (var comp in withComps.AllComps)
                if (comp is IThingHolder child)
                    foreach (Thing item in ThingOwnerUtility.GetAllThingsRecursively(child))
                        if (IsRelated(item)) return true;
        return false;
    }

    internal static void NotifyHeldArrival(Thing thing)
    {
        if (Scribe.mode != LoadSaveMode.Inactive || thing == null || thing.Destroyed) return;
        bool related = IsRelated(thing);
        if (!related && !HasHolders(thing)) return;
        if (thing.MapHeld?.GetComponent<ParagonMapRegistry>() is not { dormant: true } registry) return;
        // Active maps keep their existing 60-tick observation order. Only wake a
        // sleeping map here; spawn events still record P entry order immediately.
        if (related || ContainsRelated(thing)) Dirty(registry.map);
    }

    public static bool IsEffective(Thing part)
    {
        if (part == null || part.Destroyed || !ParagonUtility.IsParagon(part) || Kind(part) is not { } kind) return false;
        Map heldMap = part.MapHeld;
        // World/off-map items keep P history and its nominal effects. They acquire
        // map ownership when they enter a loaded map, including inside inventories.
        if (heldMap == null) return true;
        var registry = heldMap.GetComponent<ParagonMapRegistry>();
        if (registry == null) return false;
        if (Scribe.mode == LoadSaveMode.Inactive)
        {
            if (!registry.arrivalOrder.ContainsKey(part)) registry.dirty = true;
            var winner = registry.winners.GetValueOrDefault(kind);
            if (winner != null && (winner.Destroyed || winner.MapHeld is { } otherMap && otherMap != heldMap))
                registry.dirty = true;
        }
        registry.EnsureCurrent();
        return registry.effective.Contains(part);
    }

    public static bool HasUpgradeConflict(Building_ModularTurret turret, Thing part, TurretOrder currentOrder = null)
    {
        Map heldMap = turret?.MapHeld;
        string kind = Kind(part);
        if (heldMap == null || kind == null) return true;
        var registry = heldMap.GetComponent<ParagonMapRegistry>();
        if (registry == null) return true;
        registry.EnsureCurrent();
        // Recorded, temporarily unparented items still reserve their kind while
        // passing between holders. MapComponentTick ends a real off-map departure.
        if (registry.present.TryGetValue(kind, out var existing))
            foreach (Thing candidate in existing)
                if (candidate != null && !candidate.Destroyed && ParagonUtility.IsParagon(candidate)
                    && registry.arrivalOrder.ContainsKey(candidate)
                    && (candidate.MapHeld is not Map location || location == heldMap)) return true;

        // Orders are inspected live from cached holders. Adding/cancelling an order
        // cannot evade the limit within the same tick, without another map scan.
        bool isQueued = currentOrder != null && turret.Orders.Contains(currentOrder);
        foreach (Building_ModularTurret owner in registry.turrets)
        {
            if (owner == null || owner.Destroyed || owner.MapHeld != heldMap) continue;
            foreach (TurretOrder order in owner.Orders)
            {
                if (order == null || order.Kind != TurretOperation.UpgradeModule || !order.TargetParagon) continue;
                Thing reserved = owner.PartAt(order.Slot);
                if (reserved?.thingIDNumber != order.PartId || Kind(reserved) != kind) continue;
                if (!isQueued) return true;
                if (ReferenceEquals(owner, turret) && ReferenceEquals(order, currentOrder)) continue;
                if (owner.thingIDNumber < turret.thingIDNumber
                    || owner.thingIDNumber == turret.thingIDNumber && order.Id < currentOrder.Id) return true;
            }
        }
        return false;
    }

    private static string Kind(Thing part)
    {
        if (part == null || !ParagonUtility.IsEligible(part.def)) return null;
        string kind = part.def.GetModExtension<PartExtension>()?.moduleKind;
        return string.IsNullOrEmpty(kind) ? null : kind;
    }

    public override void ExposeData()
    {
        base.ExposeData();
        Scribe_Collections.Look(ref winners, "paragonMapWinners", LookMode.Value, LookMode.Reference, ref winnerKeys, ref winnerValues);
        Scribe_Collections.Look(ref arrivalOrder, "paragonMapArrivalOrder", LookMode.Reference, LookMode.Value, ref arrivalKeys, ref arrivalValues);
        Scribe_Values.Look(ref nextArrival, "paragonMapNextArrival", 1);
        if (Scribe.mode == LoadSaveMode.PostLoadInit)
        {
            winners ??= new Dictionary<string, Thing>();
            arrivalOrder ??= new Dictionary<Thing, int>();
            int highest = arrivalOrder.Count == 0 ? 0 : arrivalOrder.Values.Max();
            nextArrival = Math.Max(Math.Max(1, nextArrival), highest == int.MaxValue ? highest : highest + 1);
            dirty = true; dormant = false; lastScanTick = -1;
            // Runtime caches are rebuilt only after cross-references resolve. The
            // serialized winner remains authoritative if it is still on this map.
            present.Clear(); effective.Clear(); turrets.Clear();
        }
    }

    public override void FinalizeInit()
    {
        base.FinalizeInit();
        Unsubscribe();
        subscribedEvents = map.events;
        subscribedEvents.ThingSpawned += OnSpawned;
        subscribedEvents.ThingDespawned += OnDespawned;
        dirty = true; dormant = false;
    }

    public override void MapRemoved()
    {
        Unsubscribe();
        base.MapRemoved();
    }

    private void Unsubscribe()
    {
        if (subscribedEvents == null) return;
        subscribedEvents.ThingSpawned -= OnSpawned;
        subscribedEvents.ThingDespawned -= OnDespawned;
        subscribedEvents = null;
    }

    private void OnSpawned(Thing thing)
    {
        if (Scribe.mode != LoadSaveMode.Inactive) return;
        void Arrived(Thing item)
        {
            if (IsRelated(item) && item.MapHeld == map) Dirty(map);
            if (ParagonUtility.IsParagon(item) && item.MapHeld == map) RecordArrival(item);
        }
        Arrived(thing);
        // Spawning a pawn, pod or minified turret also brings its contents onto
        // the map. Observe entry order now, before a later periodic scan batches it.
        if (thing is IThingHolder holder)
            foreach (Thing item in ThingOwnerUtility.GetAllThingsRecursively(holder)) Arrived(item);
        if (thing is ThingWithComps withComps && withComps.AllComps != null)
            foreach (var comp in withComps.AllComps)
                if (comp is IThingHolder child)
                    foreach (Thing item in ThingOwnerUtility.GetAllThingsRecursively(child)) Arrived(item);
    }

    private void OnDespawned(Thing thing)
    {
        if (!dormant && (IsRelated(thing) || HasHolders(thing))) dirty = true;
    }

    private void RecordArrival(Thing part)
    {
        if (arrivalOrder.ContainsKey(part)) return;
        // When two map transfers occur between scans, a confirmed arrival on
        // the destination still ends the previous map's claim immediately.
        foreach (Map other in Find.Maps)
            if (other != map && other.GetComponent<ParagonMapRegistry>() is { } prior && prior.arrivalOrder.Remove(part))
            {
                if (Kind(part) is { } kind && prior.winners.GetValueOrDefault(kind) == part) prior.winners.Remove(kind);
                prior.dirty = true;
            }
        if (nextArrival == int.MaxValue)
        {
            int sequence = 1;
            foreach (Thing prior in arrivalOrder.OrderBy(p => p.Value).ThenBy(p => p.Key.thingIDNumber).Select(p => p.Key).ToList())
                arrivalOrder[prior] = sequence++;
            nextArrival = sequence;
        }
        arrivalOrder[part] = nextArrival++;
        dirty = true;
    }

    public override void MapComponentTick()
    {
        if (dormant && !dirty && !pendingDeparture) return;
        if (dirty || pendingDeparture || lastScanTick < 0 || Find.TickManager.TicksGame - lastScanTick >= 60)
            EnsureCurrent(settleTransfers: true);
    }

    private void EnsureCurrent(bool settleTransfers = false)
    {
        if (scanning || refreshing || map == null) return;
        if (Scribe.mode != LoadSaveMode.Inactive) return;
        if (dormant && !dirty && !pendingDeparture) return;
        int tick = Find.TickManager?.TicksGame ?? 0;
        if (!dirty && !(settleTransfers && pendingDeparture) && lastScanTick >= 0 && tick >= lastScanTick && tick - lastScanTick < 60) return;
        scanning = true;
        HashSet<Thing> changed = scanChanged;
        List<Building_ModularTurret> refreshCandidates = scanRefreshCandidates;
        changed.Clear(); refreshCandidates.Clear();
        try
        {
            var found = scanPresent;
            var foundTurrets = scanTurrets;
            var foundParts = scanFoundParts;
            var seenThings = scanSeenThings;
            var seenHolders = scanSeenHolders;
            var pending = scanPending;
            bool hasRelated = false;
            pending.Enqueue(map);
            void Visit(Thing thing)
            {
                if (thing == null || thing.Destroyed || !seenThings.Add(thing)) return;
                if (!hasRelated && IsRelated(thing) && thing.MapHeld == map) hasRelated = true;
                if (thing is Building_ModularTurret turret && turret.MapHeld == map) foundTurrets.Add(turret);
                if (thing.MapHeld == map && ParagonUtility.IsParagon(thing) && Kind(thing) is { } kind)
                {
                    if (!found.TryGetValue(kind, out var list)) found.Add(kind, list = ScanPartList());
                    list.Add(thing); foundParts.Add(thing);
                }
                if (thing is IThingHolder holder) pending.Enqueue(holder);
                if (thing is ThingWithComps withComps && withComps.AllComps != null)
                    foreach (var comp in withComps.AllComps)
                        if (comp is IThingHolder child) pending.Enqueue(child);
            }
            // The shared map's per-Def index contains spawned parts on every
            // floor, including spawned ASF stock. Avoid visiting unrelated ground
            // items; still traverse every child holder, including ships and custom
            // MapComponents, so unannounced nested arrivals keep the same timing.
            while (pending.Count != 0)
            {
                IThingHolder holder = pending.Dequeue();
                if (holder == null || !seenHolders.Add(holder)) continue;
                if (ReferenceEquals(holder, map) && map.listerThings != null)
                {
                    foreach (ThingDef def in RelatedDefs)
                        foreach (Thing thing in map.listerThings.ThingsOfDef(def)) Visit(thing);
                }
                else
                {
                    ThingOwner things = holder.GetDirectlyHeldThings();
                    if (things != null) foreach (Thing thing in things) Visit(thing);
                }
                var children = scanChildren;
                children.Clear();
                holder.GetChildHolders(children);
                foreach (IThingHolder child in children) if (child != null) pending.Enqueue(child);
            }
            // Keep prior arrival positions only while the item remains on this
            // map. A later re-import is a new arrival, without changing P history.
            pendingDeparture = false;
            foreach (Thing part in arrivalOrder.Keys)
                if (!foundParts.Contains(part)) scanDepartures.Add(part);
            foreach (Thing gone in scanDepartures)
            {
                // A transfer briefly unparents the same Thing between two holders.
                // Mid-operation stat queries must not elect its successor during that gap.
                if (!gone.Destroyed && ParagonUtility.IsParagon(gone) && Kind(gone) is { } kind
                    && (gone.MapHeld == map || gone.MapHeld == null && !settleTransfers))
                {
                    pendingDeparture |= gone.MapHeld == null;
                    if (!found.TryGetValue(kind, out var list)) found.Add(kind, list = ScanPartList());
                    list.Add(gone);
                }
                else arrivalOrder.Remove(gone);
            }
            // Old saves and already-present contents without an entry event use
            // the existing deterministic tie breaker. Recorded arrivals keep priority.
            if (foundParts.Count != 0)
                foreach (Thing part in foundParts.OrderBy(p => p.thingIDNumber)) RecordArrival(part);
            var selected = scanWinners;
            foreach (var group in found)
            {
                Thing winner = winners.GetValueOrDefault(group.Key);
                if (winner == null || !group.Value.Contains(winner))
                    winner = group.Value.OrderBy(p => arrivalOrder[p]).ThenBy(p => p.thingIDNumber).First();
                selected.Add(group.Key, winner);
            }
            var nextEffective = scanEffective;
            foreach (Thing part in selected.Values) nextEffective.Add(part);
            foreach (Thing part in effective) changed.Add(part);
            changed.SymmetricExceptWith(nextEffective);
            seenThings.Clear();
            foreach (Building_ModularTurret turret in turrets)
                if (turret != null && seenThings.Add(turret)) refreshCandidates.Add(turret);
            foreach (Building_ModularTurret turret in foundTurrets)
                if (turret != null && seenThings.Add(turret)) refreshCandidates.Add(turret);
            // Publish the complete cache before rebuilding any weapon. Its nested
            // P-effect queries must see the new winners and must never rescan.
            (winners, scanWinners) = (selected, winners);
            (present, scanPresent) = (found, present);
            (effective, scanEffective) = (nextEffective, effective);
            (turrets, scanTurrets) = (foundTurrets, turrets);
            // Orders belong to turrets, including minified/held turrets. Ordinary
            // heads and specialist parts also keep the registry awake, not just P.
            // If the holder hook failed, retain periodic scans rather than miss imports.
            dormant = !hasRelated && !pendingDeparture && arrivalOrder.Count == 0
                && ParagonRegistryWakePatch.Available;
            dirty = false; lastScanTick = tick;
        }
        catch { changed.Clear(); refreshCandidates.Clear(); throw; }
        finally { ClearScanScratch(); scanning = false; }

        if (changed.Count == 0) { refreshCandidates.Clear(); return; }
        refreshing = true;
        try
        {
            foreach (Building_ModularTurret turret in refreshCandidates)
                if (!turret.Destroyed && turret.Spawned && turret.MapHeld == map
                    && HoldsChangedPart(turret.GetDirectlyHeldThings(), changed)) turret.RefreshConfiguration();
            // RefreshConfiguration clamps existing shield energy; it does not fill
            // newly gained capacity. No charge or cooldown is assigned here.
        }
        finally { changed.Clear(); refreshCandidates.Clear(); refreshing = false; }
    }
}
