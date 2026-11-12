using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace ModularTurrets.Batteries;

public class WorkGiver_ModularBatteryAssembly : WorkGiver_Scanner
{
    protected virtual bool HaulingOnly => false;
    internal static bool AllowsFloorTravel(BatteryOperation kind)
        => kind is BatteryOperation.Install or BatteryOperation.Upgrade or BatteryOperation.UpgradeModule;

    // A map enters this table only when its first actionable order is queued.
    private static readonly ConditionalWeakTable<Map, List<Thing>> pendingByMap = new();
    // Claims identify the physical module, not its def or grade. Rebuild them
    // from saved orders on spawn/load; never serialize this map-owned cache.
    private static readonly ConditionalWeakTable<Map, Dictionary<int, BatteryOrder>> claimedParts = new();

    internal static bool PartClaimedByOther(Map map, int partId, BatteryOrder order = null)
        => map != null && claimedParts.TryGetValue(map, out var claims)
            && claims.TryGetValue(partId, out var owner) && owner != order;

    internal static bool SetPartClaim(Map map, BatteryOrder order, bool claim)
    {
        if (map == null || order?.Kind != BatteryOperation.Install || order.PartId <= 0) return true;
        if (claim)
        {
            var claims = claimedParts.GetOrCreateValue(map);
            return claims.TryAdd(order.PartId, order) || claims[order.PartId] == order;
        }
        if (claimedParts.TryGetValue(map, out var old) && old.TryGetValue(order.PartId, out var owner)
            && owner == order && old.Remove(order.PartId) && old.Count == 0) claimedParts.Remove(map);
        return true;
    }

    // A normal carry splits one item from a stack and gives that piece a new ID.
    internal static bool RebindPartClaim(Map map, BatteryOrder order, int partId)
    {
        if (map == null || partId <= 0 || !claimedParts.TryGetValue(map, out var claims)
            || !claims.TryGetValue(order.PartId, out var old) || old != order
            || claims.TryGetValue(partId, out var other) && other != order) return false;
        claims[partId] = order;
        claims.Remove(order.PartId);
        order.PartId = partId;
        return true;
    }
    public override PathEndMode PathEndMode => PathEndMode.Touch;

    internal static void SetPending(Building_ModularBattery battery, Map map, bool pending)
    {
        if (map == null) return;
        if (pending)
        {
            var list = pendingByMap.GetOrCreateValue(map);
            if (!list.Contains(battery)) list.Add(battery);
        }
        else if (pendingByMap.TryGetValue(map, out var list) && list.Remove(battery) && list.Count == 0)
            pendingByMap.Remove(map);
    }

    public override bool ShouldSkip(Pawn pawn, bool forced = false)
        => pawn?.Map is not { } map || !pendingByMap.TryGetValue(map, out _);

    public override IEnumerable<Thing> PotentialWorkThingsGlobal(Pawn pawn)
        => pawn?.Map is { } map && pendingByMap.TryGetValue(map, out var pending) ? pending : Array.Empty<Thing>();

    public override bool HasJobOnThing(Pawn pawn, Thing t, bool forced = false)
        => t is Building_ModularBattery battery && TryFindWork(pawn, battery, forced, out _);

    public override Job JobOnThing(Pawn pawn, Thing t, bool forced = false)
    {
        if (t is not Building_ModularBattery battery || !TryFindWork(pawn, battery, forced, out var material)) return null;
        Job job = material == null
            ? JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("MT_BatteryWork"), battery)
            : JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("MT_BatteryHaul"), material, battery);
        // Carry counts are separate: an old job must never deliver to a replacement order.
        job.count = battery.PendingOrder.Id;
        return job;
    }

    private bool TryFindWork(Pawn pawn, Building_ModularBattery battery, bool forced, out Thing material)
    {
        material = null;
        var order = battery.PendingOrder;
        if (!battery.CanReceive) return false;
        bool workReady = battery.MaterialsReady(order);
        if (HaulingOnly ? workReady : !workReady) return false;
        if (battery.IsForbidden(pawn)
            || !(AllowsFloorTravel(order.Kind) ? pawn.Map == battery.Map : Building_ModularTurret.SameFloor(pawn, battery))
            || !pawn.CanReserve(battery, 1, -1, null, forced)
            || !pawn.CanReach(battery, PathEndMode.Touch, forced ? Danger.Deadly : Danger.Some)) return false;
        if (workReady)
        {
            if (pawn.WorkTagIsDisabled(WorkTags.Constructing)) return false;
            int requiredSkill = battery.def.constructionSkillPrerequisite;
            if ((pawn.skills?.GetSkill(SkillDefOf.Construction)?.Level ?? 0) < requiredSkill)
            {
                JobFailReason.Is(L.T("MT_BatteryConstructionRequired", requiredSkill));
                return false;
            }
            return true;
        }
        if (pawn.WorkTagIsDisabled(WorkTags.Hauling)) return false;
        material = FindMaterial(pawn, battery, order, forced);
        return material != null;
    }

    private static Thing FindMaterial(Pawn pawn, Building_ModularBattery battery, BatteryOrder order, bool forced)
    {
        Thing best = null;
        int bestDistance = int.MaxValue;
        IntVec3 origin = pawn.Position;
        bool install = order.Kind == BatteryOperation.Install;
        bool floorTravel = AllowsFloorTravel(order.Kind);
        int defCount = install ? 1 : order.Costs.Count;
        for (int i = 0; i < defCount; i++)
        {
            var material = install ? order.PartDef : order.Costs[i].thingDef;
            if (material == null || battery.Needed(material) == 0 || pawn.carryTracker.MaxStackSpaceEver(material) <= 0) continue;
            var candidates = pawn.Map.listerThings.ThingsOfDef(material);
            foreach (var item in candidates)
            {
                if (install && !battery.MatchesInstallPart(order, item)) continue;
                int distance = item.Position.DistanceToSquared(origin);
                if (distance >= bestDistance || !item.Spawned || item.IsForbidden(pawn) || item.IsBurning()
                    || !(floorTravel ? item.Map == pawn.Map : Building_ModularTurret.SameFloor(pawn, item))
                    || !pawn.CanReserve(item, 1, 1, null, forced)
                    || !pawn.CanReach(item, PathEndMode.Touch, forced ? Danger.Deadly : Danger.Some)) continue;
                best = item;
                bestDistance = distance;
                if (distance == 0) return best;
            }
        }
        return best;
    }
}
