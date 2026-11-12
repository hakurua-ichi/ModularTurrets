using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using RimWorld;
using UnityEngine;
using Verse;

namespace ModularTurrets;

/// <summary>Plans the whole map before Core can refund an expanded turret.</summary>
public sealed class MapComponent_FootprintMigration : MapComponent, IThingHolder
{
    private static readonly FieldInfo LoadedThings = typeof(Map).GetField("loadedFullThings", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo CompressedData = typeof(MapFileCompressor).GetField("compressedData", BindingFlags.Instance | BindingFlags.NonPublic);
    private ThingOwner<Thing> pending;
    private DisplacedBuildings displaced;
    private List<FootprintMigrationEvent> history = new();
    private string lastFailure;
    private int migrationVersion, loggedHistoryCount;
    private bool noticePending, cleanDisplaced;
    private List<Thing> awaitingSpawn;
    public ThingOwner<Thing> Pending => pending ??= new ThingOwner<Thing>(this);
    private ThingOwner<Thing> Displaced => (displaced ??= new DisplacedBuildings(this)).Contents;
    public IReadOnlyList<FootprintMigrationEvent> History => history;
    public string LastFailure => lastFailure;
    public int MigrationVersion => migrationVersion;
    public IThingHolder ParentHolder => map;
    public ThingOwner GetDirectlyHeldThings() => Pending;
    public void GetChildHolders(List<IThingHolder> outChildren)
    {
        ThingOwnerUtility.AppendThingHoldersFromThings(outChildren, Pending);
        // Retain objects displaced by older versions until an explicit cleanup.
        if (displaced != null) outChildren.Add(displaced);
    }

    private sealed class DisplacedBuildings : IThingHolder, IExposable
    {
        private ThingOwner<Thing> contents;
        public IThingHolder ParentHolder { get; }
        public ThingOwner<Thing> Contents => contents ??= new ThingOwner<Thing>(this);
        public DisplacedBuildings(IThingHolder parent) { ParentHolder = parent; }
        public ThingOwner GetDirectlyHeldThings() => Contents;
        public void GetChildHolders(List<IThingHolder> children) => ThingOwnerUtility.AppendThingHoldersFromThings(children, Contents);
        public void ExposeData() => Scribe_Deep.Look(ref contents, "contents", this);
    }

    public MapComponent_FootprintMigration(Map map) : base(map)
    {
        // New components absent from old XML are not automatically PostLoadInit'd.
        // Register a separate object: Scribe_Deep may also register the component.
        if (Scribe.mode == LoadSaveMode.LoadingVars)
            Scribe.loader.initer.RegisterForPostLoadInit(new DeferredPostLoad(this));
    }

    private sealed class DeferredPostLoad : IExposable
    {
        private readonly MapComponent_FootprintMigration owner;
        public DeferredPostLoad(MapComponent_FootprintMigration owner) { this.owner = owner; }
        public void ExposeData()
        {
            // ConstructComponents' temporary instance may be replaced by saved XML.
            if (Scribe.mode == LoadSaveMode.PostLoadInit && owner.map.components.Any(c => ReferenceEquals(c, owner)))
                owner.PrepareAfterLoad();
        }
    }

    public override void ExposeData()
    {
        Scribe_Deep.Look(ref pending, "pendingTurrets", this);
        Scribe_Deep.Look(ref displaced, "displacedBuildings", this);
        Scribe_Collections.Look(ref history, "footprintHistory", LookMode.Deep);
        Scribe_Values.Look(ref lastFailure, "lastFailure");
        Scribe_Values.Look(ref migrationVersion, "migrationVersion");
        Scribe_Values.Look(ref loggedHistoryCount, "loggedHistoryCount");
        Scribe_Values.Look(ref cleanDisplaced, "cleanDisplaced");
        if (Scribe.mode == LoadSaveMode.PostLoadInit) history ??= new();
    }

    private static bool IsOur(Thing thing)
    {
        ThingDef built = thing?.def?.entityDefToBuild as ThingDef ?? thing?.def;
        return built?.defName is "MT_TurretBase" or "MT_TurretBaseLarge";
    }
    private static bool Obstacle(Thing thing) => thing.def.category == ThingCategory.Building || thing.def.IsBlueprint;
    private static CellRect Footprint(IntVec3 position) => GenAdj.OccupiedRect(position, Rot4.North, new IntVec2(3, 3));
    private static FootprintMigrationEvent Event(Thing thing, string reason, IntVec3 destination)
        => new() { DefName = thing.def.defName, ThingId = thing.ThingID, From = thing.Position, To = destination, Reason = reason };

    private sealed class Plan
    {
        public readonly Dictionary<Thing, IntVec3> Positions = new();
        public readonly List<FootprintMigrationEvent> Events = new();
    }

    private bool TryBuildPlan(List<Thing> targets, List<Thing> others, byte[] compressed, bool live, out Plan plan, out string failure)
    {
        plan = new Plan(); failure = null;
        if (targets.Select(t => t.ThingID).Distinct().Count() != targets.Count || targets.Any(t => t.Destroyed || t.def.size != new IntVec2(3, 3)))
        { failure = L.T("MT_MigrationInvalidTargets"); return false; }
        var reservations = new Dictionary<int, HashSet<IntVec3>>();
        HashSet<IntVec3> Reserved(int floor)
        {
            if (!reservations.TryGetValue(floor, out var cells)) reservations.Add(floor, cells = new());
            return cells;
        }
        ThingDef builtDef = targets[0].def.entityDefToBuild as ThingDef ?? targets[0].def;
        bool Conflicts(ThingDef other) => other.IsBlueprint || GenSpawn.SpawningWipes(builtDef, other) || GenSpawn.SpawningWipes(other, builtDef);
        foreach (Thing other in others.Where(t => Obstacle(t) && (IsOur(t) || Conflicts(t.def))))
        {
            if (!Building_ModularTurret.TryGetFloor(other, out int floor))
            { failure = L.T("MT_MigrationUnknownFloor"); return false; }
            foreach (IntVec3 cell in other.OccupiedRect()) Reserved(floor).Add(cell);
        }
        if (!live && compressed != null)
        {
            var cells = new Dictionary<IntVec3, ushort>();
            MapSerializeUtility.LoadUshort(compressed, map, (cell, hash) => cells[cell] = hash);
            var defs = DefDatabase<ThingDef>.AllDefs.GroupBy(d => d.shortHash).ToDictionary(g => g.Key, g => g.First());
            foreach (var entry in cells.ToList())
            {
                IntVec3 cell = entry.Key; ushort hash = entry.Value;
                if (hash == 0) continue;
                ThingDef def = BackCompatibility.BackCompatibleThingDefWithShortHash_Force(hash, ScribeMetaHeaderUtility.loadedGameVersionMajor, ScribeMetaHeaderUtility.loadedGameVersionMinor);
                if (def == null) defs.TryGetValue(hash, out def);
                def ??= BackCompatibility.BackCompatibleThingDefWithShortHash(hash);
                if (def == null) { failure = L.T("MT_MigrationUnknownCompressed", hash, cell); return false; }
                if (def.category != ThingCategory.Building || !Conflicts(def)) continue;
                // Logical Floors saves upper-floor things uncompressed. Core's
                // compressed grid belongs to floor 0; never rewrite its bytes.
                foreach (IntVec3 occupied in GenAdj.OccupiedRect(cell, Rot4.North, def.size)) Reserved(0).Add(occupied);
            }
        }
        foreach (Thing target in targets.OrderBy(t => t.thingIDNumber).ThenBy(t => t.def.defName, StringComparer.Ordinal))
        {
            if (!Building_ModularTurret.TryGetFloor(target, out int floor))
            { failure = L.T("MT_MigrationUnknownFloor"); return false; }
            var reserved = Reserved(floor);
            if (!TryFindPosition(target.Position, reserved, out IntVec3 destination))
            { failure = L.T("MT_MigrationNoPlacement"); return false; }
            plan.Positions.Add(target, destination);
            foreach (IntVec3 cell in Footprint(destination)) reserved.Add(cell);
            if (destination != target.Position) plan.Events.Add(Event(target, "MT_MigrationMoved", destination));
        }
        return true;
    }

    private bool TryFindPosition(IntVec3 original, HashSet<IntVec3> reserved, out IntVec3 result)
    {
        result = IntVec3.Invalid;
        if (map.Size.x < 3 || map.Size.z < 3) return false;
        bool Clear(IntVec3 cell) => !Footprint(cell).Any(reserved.Contains);
        var clamped = new IntVec3(Math.Clamp(original.x, 1, map.Size.x - 2), original.y, Math.Clamp(original.z, 1, map.Size.z - 2));
        if (Clear(clamped)) { result = clamped; return true; }
        long nearest = long.MaxValue;
        // Exhaustive finite search: smallest squared distance, then Z/X ties.
        // Failure means this deterministic reservation plan failed, not a packing proof.
        for (int z = 1; z < map.Size.z - 1; z++)
        for (int x = 1; x < map.Size.x - 1; x++)
        {
            long dx = (long)x - original.x, dz = (long)z - original.z, distance = dx * dx + dz * dz;
            if (distance >= nearest) continue;
            var cell = new IntVec3(x, original.y, z);
            if (!Clear(cell)) continue;
            result = cell; nearest = distance;
        }
        return result.IsValid;
    }

    private void PrepareAfterLoad()
    {
        // Callback registration order need not place this component's own
        // PostLoadInit before the separate deferred callback.
        history ??= new();
        // Version 1 is already 3x3. Re-running against all loaded floors can
        // damage an otherwise valid map and is not a migration.
        if (migrationVersion >= 1) { noticePending |= Pending.Count > 0 || Displaced.Count > 0; return; }
        // The private-field contract is verified for supported Core 1.7.4998.
        // An unsupported engine cannot be reported as a successful preservation.
        if (LoadedThings == null || LoadedThings.FieldType != typeof(List<Thing>))
        {
            lastFailure = L.T("MT_MigrationUnsupportedEngine"); noticePending = true;
            Log.Error(lastFailure); return;
        }
        List<Thing> full = (List<Thing>)LoadedThings.GetValue(map);
        if (full == null) return;
        var targets = full.Where(IsOur).ToList();
        if (targets.Count == 0) { migrationVersion = 1; noticePending |= Pending.Count > 0; return; }
        List<Thing> originalList = full.ToList();
        var originalPositions = targets.Distinct().ToDictionary(t => t, t => t.Position);
        byte[] originalCompressed = null;
        int historyCount = history.Count;
        try
        {
            if (targets.Any(t => Pending.Contains(t))) throw new InvalidOperationException(L.T("MT_MigrationInvalidTargets"));
            if (CompressedData == null || CompressedData.FieldType != typeof(byte[]) || map.compressor == null)
                throw new InvalidOperationException(L.T("MT_MigrationUnsupportedEngine"));
            originalCompressed = (byte[])CompressedData.GetValue(map.compressor);
            if (!TryBuildPlan(targets, full.Except(targets).ToList(), originalCompressed, false, out Plan plan, out string failure))
            { Quarantine(full, targets, failure); return; }
            CommitLoadPlan(plan);
            awaitingSpawn = targets;
            noticePending |= plan.Events.Count > 0;
            if (Pending.Count == 0) lastFailure = null;
        }
        catch (Exception error)
        {
            // Obstacles and compressed data are never mutated. Restore positions
            // and the load list before retaining every affected turret.
            full.Clear(); full.AddRange(originalList);
            foreach (var original in originalPositions) original.Key.SetPositionDirect(original.Value);
            if (history.Count > historyCount) history.RemoveRange(historyCount, history.Count - historyCount);
            cleanDisplaced = false; awaitingSpawn = null;
            Quarantine(full, targets, L.T("MT_MigrationInternalFailure", error.GetType().Name));
            ModDiagnostics.ErrorOnce("Construction.FootprintMigration", error);
        }
    }

    private void CommitLoadPlan(Plan plan)
    {
        foreach (var placement in plan.Positions) placement.Key.SetPositionDirect(placement.Value);
        history.AddRange(plan.Events);
    }

    private void Quarantine(List<Thing> full, List<Thing> targets, string reason)
    {
        foreach (Thing target in targets.Distinct())
        {
            if (!Pending.Contains(target) && !Pending.TryAdd(target, false))
                throw new InvalidOperationException(L.T("MT_MigrationHoldingFailed", target.ThingID));
        }
        full.RemoveAll(targets.Contains);
        lastFailure = reason; noticePending = true; migrationVersion = 1;
        history.AddRange(targets.Distinct().Select(t => Event(t, "MT_MigrationQuarantined", t.Position)));
    }

    public override void FinalizeInit()
    {
        if (awaitingSpawn != null)
        {
            if (awaitingSpawn.All(t => !t.Destroyed && t.Spawned && t.Map == map)) migrationVersion = 1;
            else { lastFailure = L.T("MT_MigrationSpawnIncomplete"); noticePending = true; cleanDisplaced = false; }
            awaitingSpawn = null;
        }
        // Old saves may contain displaced obstacles and a queued destruction.
        // Keep that saved root; loading a map must not finish deleting it.
        cleanDisplaced = false;
        noticePending |= Pending.Count > 0 || Displaced.Count > 0;
        if (noticePending) NotifyResult();
    }
    public override void MapGenerated() { migrationVersion = 1; }

    private void CleanupDisplaced()
    {
        foreach (Thing victim in Displaced.ToList())
        {
            try { victim.Destroy(DestroyMode.Vanish); }
            catch (Exception error) { lastFailure = L.T("MT_MigrationCleanupFailure", victim.ThingID); ModDiagnostics.ErrorOnce("Construction.FootprintMigration", error); noticePending = true; }
        }
        cleanDisplaced = false;
    }

    private void NotifyResult()
    {
        noticePending = false;
        string Line(FootprintMigrationEvent e) => L.T("MT_MigrationEventLine", e.DefName, string.IsNullOrEmpty(e.ThingId) ? L.T("MT_MigrationCompressedId") : e.ThingId, e.From, e.To, L.T(e.Reason));
        string details = string.Join("\n", history.TakeLast(24).Select(Line));
        if (lastFailure == null && (Pending.Count > 0 || Displaced.Count > 0)) lastFailure = L.T("MT_MigrationPendingRemain");
        string body = lastFailure == null
            ? L.T("MT_MigrationSuccess", details)
            : L.T("MT_MigrationFailure", Pending.Count, lastFailure, details);
        Log.Message(L.T("MT_MigrationLog", lastFailure ?? L.T("MT_MigrationTitle")));
        // The saved history is complete; only newly recorded events are appended
        // to the log, so loading a quarantined map does not repeat old damage lists.
        foreach (var entry in history.Skip(Math.Clamp(loggedHistoryCount, 0, history.Count))) Log.Message(Line(entry));
        loggedHistoryCount = history.Count;
        Find.LetterStack?.ReceiveLetter(L.T("MT_MigrationTitle"), body, lastFailure == null ? LetterDefOf.NeutralEvent : LetterDefOf.NegativeEvent, new TargetInfo(map.Center, map));
    }

    public bool RetryPending()
    {
        var targets = Pending.ToList(); if (targets.Count == 0) return true;
        var positions = targets.ToDictionary(t => t, t => t.Position);
        try
        {
            // Manual retries use vacant cells; existing buildings are fixed.
            if (!TryBuildPlan(targets, map.listerThings.AllThings.ToList(), null, true, out Plan plan, out string failure))
            { lastFailure = failure; NotifyResult(); return false; }
            foreach (var placement in plan.Positions)
            {
                Pending.Remove(placement.Key);
                if (GenSpawn.Spawn(placement.Key, placement.Value, map, placement.Key.Rotation, WipeMode.Vanish, respawningAfterLoad: true) != placement.Key || !placement.Key.Spawned)
                    throw new InvalidOperationException(L.T("MT_MigrationSpawnIncomplete"));
            }
            history.AddRange(plan.Events); history.AddRange(targets.Select(t => Event(t, "MT_MigrationRestored", t.Position)));
            lastFailure = null; migrationVersion = 1; NotifyResult(); return true;
        }
        catch (Exception error)
        {
            foreach (Thing target in targets)
            {
                if (target.Spawned) target.DeSpawn();
                target.SetPositionDirect(positions[target]);
                if (!Pending.Contains(target) && !Pending.TryAdd(target, false)) Log.Error(L.T("MT_MigrationHoldingFailed", target.ThingID));
            }
            lastFailure = L.T("MT_MigrationInternalFailure", error.GetType().Name); NotifyResult();
            ModDiagnostics.ErrorOnce("Construction.FootprintMigration", error); return false;
        }
    }

    public override void MapComponentOnGUI()
    {
        if (Find.CurrentMap != map || Pending.Count == 0 && Displaced.Count == 0) return;
        var panel = new Rect(12f, 80f, 420f, Displaced.Count > 0 ? 108f : 72f);
        Widgets.DrawWindowBackground(panel);
        Widgets.Label(new Rect(panel.x + 8f, panel.y + 5f, panel.width - 16f, 28f), L.T("MT_MigrationPendingCount", Pending.Count));
        if (Pending.Count > 0 && Widgets.ButtonText(new Rect(panel.x + 8f, panel.y + 35f, panel.width - 16f, 29f), L.T("MT_MigrationRetry"))) RetryPending();
        if (Displaced.Count > 0 && Widgets.ButtonText(new Rect(panel.x + 8f, panel.y + 70f, panel.width - 16f, 29f), L.T("MT_MigrationCleanupRetry", Displaced.Count)))
        { CleanupDisplaced(); if (Displaced.Count == 0 && Pending.Count == 0) lastFailure = null; NotifyResult(); }
    }
}
