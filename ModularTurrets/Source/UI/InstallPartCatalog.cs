using System;
using System.Collections.Generic;
using System.Linq;
using ModularTurrets.Batteries;
using RimWorld;
using Verse;
using Verse.AI;

namespace ModularTurrets;

// UI-owned snapshot. No map component, global item census or game Tick work.
internal sealed class InstallPartCatalog
{
    private static readonly string[] RomanLevels = { "I", "II", "III", "IV", "V" };
    internal sealed class Entry
    {
        internal readonly ThingDef Def;
        internal readonly int Level;
        internal readonly bool Paragon;
        internal readonly List<Thing> Local = new();
        internal Thing Preview, Item;
        internal int LocalCount, AvailableCount, OtherMapCount;
        internal string OtherMaps;
        internal bool Base => Level == 0 && !Paragon;
        internal Entry(ThingDef def, int level, bool paragon) { Def = def; Level = level; Paragon = paragon; }
        internal string Grade => Paragon ? L.T("MT_ParagonLabel") : Base ? L.T("MT_InstallBaseGrade")
            : L.T("MT_InstallGrade", Level is >= 1 and <= 5 ? RomanLevels[Level - 1] : Level.ToString());
        internal string Stock => L.T("MT_InstallStock", LocalCount, AvailableCount, OtherMapCount);
        internal string Status => Item != null ? null : LocalCount > 0 ? L.T("MT_InstallLocalUnavailable")
            : OtherMapCount > 0 ? L.T("MT_InstallOtherMapOnly") : L.T("MT_InstallCraftNeeded");
    }

    private readonly Thing owner;
    private readonly Func<Thing, (int level, bool paragon)> grade;
    private readonly Func<Thing, bool> available;
    private readonly List<Entry> entries = new();
    private readonly HashSet<int> claimed = new();
    private int nextRefreshFrame;
    internal IReadOnlyList<Entry> Entries => entries;

    internal InstallPartCatalog(Thing owner, IEnumerable<ThingDef> definitions,
        Func<Thing, (int level, bool paragon)> grade, Func<Thing, bool> available)
    {
        this.owner = owner; this.grade = grade; this.available = available;
        foreach (var def in definitions.OrderBy(d => L.Label(d)).ThenBy(d => d.defName))
        {
            var groups = new Dictionary<(int level, bool paragon), Entry>
                { [(0, false)] = new Entry(def, 0, false) };
            var mapNames = new Dictionary<Entry, HashSet<string>>();
            foreach (var map in Find.Maps)
            foreach (var part in map.listerThings.ThingsOfDef(def))
            {
                if (!part.Spawned || part.Destroyed) continue;
                var stage = Stage(part);
                if (!groups.TryGetValue(stage, out var entry)) groups[stage] = entry = new Entry(def, stage.level, stage.paragon);
                entry.Preview ??= part;
                if (map == owner.Map) entry.Local.Add(part);
                else
                {
                    entry.OtherMapCount += part.stackCount;
                    if (!mapNames.TryGetValue(entry, out var names)) mapNames[entry] = names = new();
                    names.Add(map.Parent?.LabelCap.ToString() ?? map.uniqueID.ToString());
                }
            }
            foreach (var entry in groups.Values.OrderBy(e => e.Paragon).ThenBy(e => e.Level))
            {
                if (mapNames.TryGetValue(entry, out var names)) entry.OtherMaps = L.T("MT_InstallOtherMaps", string.Join(", ", names.OrderBy(n => n)));
                entries.Add(entry);
            }
        }
        RefreshAvailability(true);
    }

    private (int level, bool paragon) Stage(Thing part)
    {
        var value = grade(part);
        return (value.paragon ? 5 : Math.Max(0, value.level), value.paragon);
    }

    internal void RefreshAvailability(bool force = false)
    {
        if (!force && RealTime.frameCount < nextRefreshFrame) return;
        nextRefreshFrame = RealTime.frameCount + 30;
        claimed.Clear();
        foreach (var building in owner.Map.listerBuildings.allBuildingsColonist)
        {
            if (building is Building_ModularTurret turret)
                foreach (var order in turret.Orders)
                    if (order.Kind == TurretOperation.Install && order.PartId > 0) claimed.Add(order.PartId);
            if (building is Building_ModularBattery battery)
                foreach (var order in battery.AssemblyOrders)
                    if (order.Kind == BatteryOperation.Install && order.PartId > 0) claimed.Add(order.PartId);
        }
        foreach (var entry in entries)
        {
            entry.Item = null; entry.LocalCount = entry.AvailableCount = 0;
            foreach (var part in entry.Local)
            {
                if (!part.Spawned || part.Destroyed || part.Map != owner.Map || Stage(part) != (entry.Level, entry.Paragon)) continue;
                entry.LocalCount += part.stackCount;
                if (!Available(part)) continue;
                entry.AvailableCount += part.stackCount;
                float distance = part.Position.DistanceToSquared(owner.Position);
                if (entry.Item == null || distance < entry.Item.Position.DistanceToSquared(owner.Position)
                    || distance == entry.Item.Position.DistanceToSquared(owner.Position) && part.thingIDNumber < entry.Item.thingIDNumber)
                    entry.Item = part;
            }
        }
    }

    private bool Available(Thing part)
    {
        if (part.stackCount <= 0 || part.stackCount != 1 && owner is not Building_ModularBattery
            || part.Map != owner.Map || claimed.Contains(part.thingIDNumber) || !available(part)
            || !FloorCompatibility.TryGetFloor(owner, out int floor)
            || !FloorCompatibility.TryGetFloor(part, out int partFloor)
            || !FloorCompatibility.TryEnter(part.Map, partFloor, out var scope)) return false;
        using (scope)
        {
            if (part.Map.reservationManager.IsReserved(part) || part.Map.physicalInteractionReservationManager.IsReserved(part)) return false;
            // A catalog has no worker: only the real pawn's Thing path can
            // establish a route through stairs to a different floor.
            return floor != partFloor || owner.Map.reachability.CanReach(part.Position, owner,
                PathEndMode.Touch, TraverseMode.PassDoors, Danger.Deadly);
        }
    }

    internal Thing Resolve(Entry entry)
    {
        // The window may stay open while a pawn reserves or moves the item.
        // Recheck at the click; the hauling driver owns reachability and pickup.
        RefreshAvailability(true);
        return entry.Item;
    }
}
