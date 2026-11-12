using System;
using System.Collections.Generic;
using CombatExtended;
using HarmonyLib;
using RimWorld;
using Verse;

namespace ModularTurrets.Compat.CombatExtended;

internal sealed partial class CombatAdapter
{
    internal static bool AllowAutomaticMortarJob(Thing thing, bool forced, ref bool __result)
    {
        if (forced || thing is not Building_ModularTurret { AutoReload: false } turret || turret.Faction != Faction.OfPlayer
            || turret.HeadPart?.def.defName != "MT_Head_DualMortar") return true;
        __result = false;
        return false;
    }

    private static FireChannel MortarChannel(Building_ModularTurret owner)
    {
        if (owner is not { Spawned: true } || owner.IsWrecked || owner.Faction != Faction.OfPlayer) return null;
        var channels = owner.CombatChannels;
        for (int i = 0; i < channels.Count; i++)
            if (channels[i].Slot == "head" && channels[i].Verb is Verb_MountedMortarCE) return channels[i];
        return null;
    }

    private static CompAmmoUser MortarMagazine(Building_ModularTurret owner)
        => MortarChannel(owner)?.Gun?.TryGetComp<CompAmmoUser>() is { UseAmmo: true, HasMagazine: true } ammo
            && ammo.turret == owner ? ammo : null;

    private static bool AllowedAmmo(CompAmmoUser ammo, ThingDef definition)
    {
        if (definition == null || ammo?.CurAmmoSet?.ammoTypes is not { } links) return false;
        foreach (var link in links)
            if (link?.ammo == definition && link.projectile != null) return true;
        return false;
    }

    public IEnumerable<ThingDef> MortarAmmoTypes(Building_ModularTurret owner)
    {
        if (MortarMagazine(owner)?.CurAmmoSet?.ammoTypes is not { } links) yield break;
        foreach (var link in links)
            if (link?.ammo != null && link.projectile != null) yield return link.ammo;
    }
    public ThingDef SelectedMortarAmmo(Building_ModularTurret owner) => MortarMagazine(owner)?.SelectedAmmo;
    public int LoadedMortarRounds(Building_ModularTurret owner) => MortarMagazine(owner)?.CurMagCount ?? 0;
    public ThingDef LoadedMortarAmmo(Building_ModularTurret owner)
        => MortarMagazine(owner) is { CurMagCount: > 0 } ammo ? ammo.CurrentAmmo : null;

    private bool ReservedForWork(Building_ModularTurret owner) => GetReloading(owner)
        || owner.Map.reservationManager.IsReserved(owner) || owner.Map.physicalInteractionReservationManager.IsReserved(owner);

    public bool TrySelectMortarAmmo(Building_ModularTurret owner, ThingDef ammunition)
    {
        var ammo = MortarMagazine(owner);
        if (ammunition is not AmmoDef selected || !AllowedAmmo(ammo, selected)) return false;
        if (ammo.SelectedAmmo == selected) return true;
        if (ReservedForWork(owner)) return false;
        // CE's unmanned turret selector changes the next load, leaving current rounds in place.
        ammo.SelectedAmmo = selected;
        return true;
    }

    public bool CanUnloadMortarAmmo(Building_ModularTurret owner) => MortarMagazine(owner) is { CurMagCount: > 0 } ammo
        && ammo.CurrentAmmo is { ammoCount: > 0 } current
        && (ammo.CurMagCount % current.ammoCount == 0 || current.HasComp(typeof(CompApparelReloadable))
            || current.partialUnloadAmmoDef != null && current.partialUnloadAmmoDef != current)
        && owner.PendingOrder == null && !MortarChannel(owner).Verb.Bursting && !ReservedForWork(owner);

    public bool TryUnloadMortarAmmo(Building_ModularTurret owner)
    {
        if (!CanUnloadMortarAmmo(owner) || !FloorCompatibility.TryGetFloor(owner, out int floor)
            || !FloorCompatibility.TryEnter(owner.Map, floor, out var scope)) return false;
        using (scope)
        {
            var ammo = MortarMagazine(owner);
            var type = ammo.CurrentAmmo;
            int before = ammo.CurMagCount;
            if (!HasDropSpace(owner, type, before)) return false;
            bool reload = owner.AutoReload;
            owner.AutoReload = false;
            try
            {
                if (before < type.ammoCount)
                {
                    // CE also creates a zero-sized full-ammo stack for a remainder-only unload.
                    // Return only the charged item or the physical partial-ammo Def CE specifies.
                    bool charged = type.HasComp(typeof(CompApparelReloadable));
                    var partial = ThingMaker.MakeThing(charged ? type : type.partialUnloadAmmoDef);
                    if (charged)
                    {
                        // CE's publicized build writes this Core field when returning a partly used charge.
                        var charges = AccessTools.Field(typeof(CompApparelReloadable), "remainingCharges");
                        if (charges == null) { partial.Destroy(); return false; }
                        charges.SetValue(partial.TryGetComp<CompApparelReloadable>(), before);
                    }
                    else partial.stackCount = before;
                    int placed = 0;
                    if (FloorCompatibility.TrySetFloor(partial, floor))
                        GenPlace.TryPlaceThing(partial, owner.Position, owner.Map, ThingPlaceMode.Near, (_, n) => placed += n);
                    if (!partial.Spawned && !partial.Destroyed) partial.Destroy();
                    ammo.CurMagCount = before - (charged ? placed * before : placed);
                }
                else
                {
                    // CE clears its counter even if a full map denied some drops. Reconcile exactly
                    // what entered this floor, including partial stacks merged into existing ammo.
                    long initial = GroundCharges(owner.Map, floor, type);
                    try { ammo.TryUnload(out _, true); }
                    finally
                    {
                        int returned = (int)Math.Clamp(GroundCharges(owner.Map, floor, type) - initial, 0L, before);
                        ammo.CurMagCount = before - returned;
                    }
                }
                return ammo.CurMagCount == 0;
            }
            finally
            {
                if (ammo.CurMagCount == before) owner.AutoReload = reload;
            }
        }
    }

    // A cold check for the same nearby-cell search CE requests through GenPlace.
    // Existing stacks count as candidates even in a full shelf. Final accounting
    // also covers storage filters changing or another mod declining placement.
    private static bool HasDropSpace(Building_ModularTurret owner, AmmoDef type, int rounds)
    {
        bool fullItem = rounds >= type.ammoCount;
        bool partialItem = rounds % type.ammoCount != 0;
        ThingDef partial = type.HasComp(typeof(CompApparelReloadable)) ? type : type.partialUnloadAmmoDef;
        Map map = owner.Map;
        foreach (var c in GenRadial.RadialCellsAround(owner.Position, 12.9f, true))
        {
            if (!c.InBounds(map)) continue;
            if (c.GetItemCount(map) < c.GetMaxItemsAllowedInCell(map))
            {
                if (fullItem && GenSpawn.CanSpawnAt(type, c, map, type.defaultPlacingRot)) fullItem = false;
                if (partialItem && GenSpawn.CanSpawnAt(partial, c, map, partial.defaultPlacingRot)) partialItem = false;
            }
            else
                foreach (var t in c.GetThingList(map))
                    if (t.stackCount < t.def.stackLimit)
                    {
                        if (t.def == type) fullItem = false;
                        if (t.def == partial) partialItem = false;
                    }
            if (!fullItem && !partialItem) return true;
        }
        return false;
    }

    private static long GroundCharges(Map map, int floor, AmmoDef type)
    {
        long total = 0;
        foreach (var item in map.listerThings.ThingsOfDef(type))
            if (FloorCompatibility.TryGetFloor(item, out int itemFloor) && floor == itemFloor)
                total += (long)item.stackCount * (item.TryGetComp<CompApparelReloadable>()?.RemainingCharges ?? type.ammoCount);
        if (type.partialUnloadAmmoDef is { } partial && partial != type && !type.HasComp(typeof(CompApparelReloadable)))
            foreach (var item in map.listerThings.ThingsOfDef(partial))
                if (FloorCompatibility.TryGetFloor(item, out int itemFloor) && floor == itemFloor) total += item.stackCount;
        return total;
    }
}
