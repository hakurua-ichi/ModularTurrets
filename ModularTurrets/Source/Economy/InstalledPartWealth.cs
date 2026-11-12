using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using ModularTurrets.Batteries;
using RimWorld;
using Verse;

namespace ModularTurrets;

// Shared only by wealth collection call sites. A part's ordinary MarketValue is
// unchanged, including while the building calculates its embedded part value.
public static class InstalledPartWealth
{
    private static readonly Predicate<Thing> EmbeddedPart = IsEmbeddedPart;
    private static bool ready, blocked;
    public static bool TransferEnabled => ready && !blocked;

    internal static void MarkReady()
    {
        if (ready || blocked) return;
        ready = true;
        ClearValueCaches();
    }

    public static void BlockTransfer()
    {
        if (blocked) return;
        blocked = true;
        ClearValueCaches();
    }

    private static void ClearValueCaches()
    {
        StatDefOf.MarketValue?.Worker?.TryClearCache();
        StatDefOf.MarketValueIgnoreHp?.Worker?.TryClearCache();
    }

    public static bool IsEmbeddedPart(Thing part)
    {
        if (!TransferEnabled) return false;
        var owner = part?.ParentHolder;
        if (owner is Building_ModularTurret turret)
            return turret.IsFittedWealthPart(part) && CountedWithOwner(turret);
        return owner is Building_ModularBattery battery && CountedWithOwner(battery)
            && (battery.ModuleAt("left") == part || battery.ModuleAt("right") == part);
    }

    private static bool CountedWithOwner(Thing owner)
    {
        // Spawned enemy buildings are absent from Core's building wealth; their
        // contained items must not disappear from the item tally.
        if (owner.Spawned) return owner.Faction == Faction.OfPlayer;
        // A minified building is counted as an item instead, and its market
        // value already includes the same embedded parts.
        return owner.ParentHolder is MinifiedThing minified && minified.InnerThing == owner
            && minified.SpawnedOrAnyParentSpawned && ThingRequestGroup.HaulableEver.Includes(minified.def);
    }

    public static void RemoveEmbeddedParts(List<Thing> things)
    {
        if (TransferEnabled) things.RemoveAll(EmbeddedPart);
    }

    // Optional breakdowns can preserve their original visibility/filter rules
    // while applying the same removal before both quantity and value are summed.
    public static int RemoveExcludedItems(List<Thing> things, Predicate<Thing> originalFilter)
        => things.RemoveAll(originalFilter) + (TransferEnabled ? things.RemoveAll(EmbeddedPart) : 0);
}
