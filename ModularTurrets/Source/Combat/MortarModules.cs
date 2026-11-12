using System;
using RimWorld;
using UnityEngine;
using Verse;

namespace ModularTurrets;

public sealed partial class Building_ModularTurret
{
    private SpecialistModuleExtension mortarPayload, mortarSupport;
    private Thing mortarGun;
    private int nextMortarReloadTick;

    public float MortarPayloadDamageFactor(Thing equipment)
        => equipment != null && equipment == mortarGun && mortarPayload != null
            ? SpecialistModuleExtension.Factor(mortarPayload.damageFactor, 1f) : 1f;
    public float MortarExplosionRadiusFactor(Thing equipment)
        => equipment != null && equipment == mortarGun && mortarPayload != null
            ? SpecialistModuleExtension.Factor(mortarPayload.explosionRadiusFactor, 1f) : 1f;
    public float MortarSupportWarmupFactor => mortarSupport != null
        ? SpecialistModuleExtension.Factor(mortarSupport.warmupFactor, 1f) : 1f;
    private float MortarSupportCooldownFactor => mortarSupport != null
        ? SpecialistModuleExtension.Factor(mortarSupport.cooldownFactor, 1f) : 1f;
    private bool RapidCoreMortarProjectiles
        => CombatCompatibility.Provider == null && mortarSupport != null && mortarGun != null;

    private void RefreshMortarModules()
    {
        mortarPayload = mortarSupport = null;
        mortarGun = null;
        if (HeadPart?.def.defName != "MT_Head_DualMortar") return;
        // Different dedicated modules may coexist; the legacy single-specialist
        // lookup must not let the first module hide the other module's effect.
        foreach (var mount in fitted)
        {
            if (mount.Slot == "head" || mount.Part == null || !CanMountPart(mount.Slot, mount.Part.def)) continue;
            var specialist = mount.Part.def.GetModExtension<SpecialistModuleExtension>();
            if (specialist?.kind == "MortarPayload") mortarPayload ??= specialist;
            else if (specialist?.kind == "MortarSupport") mortarSupport ??= specialist;
        }
    }

    private void RefreshMortarGun()
    {
        if (mortarPayload == null && mortarSupport == null) return;
        foreach (var channel in channels)
            if (channel.Slot == "head" && channel.Part == HeadPart) { mortarGun = channel.Gun; break; }
    }

    private bool CanMortarAutoload => mortarSupport != null && mortarGun != null && Spawned
        && Faction == Faction.OfPlayer && AutoReload && Powered && !IsStunned
        && !AssemblyBlocked && !this.IsBurning() && CombatCompatibility.IsAvailable;

    private void TickMortarAutoReload()
    {
        // Called only from this turret's existing 60-tick hash interval.
        // Maps without the module gain no polling component or global hook.
        if (!CanMortarAutoload || Find.TickManager.TicksGame < nextMortarReloadTick) return;
        bool loaded = false;
        if (CombatCompatibility.Provider is { } provider)
        {
            if (provider is not IMortarAutoloadBackend backend) return;
            foreach (var channel in channels)
                if (channel.Slot == "head" && channel.Gun == mortarGun)
                { loaded = backend.TryAutoReload(this, channel); break; }
        }
        else
        {
            if (Shells >= 20) return;
            if (PendingOrder?.Kind != TurretOperation.Reload && !QueueReload()) return;
            var order = PendingOrder;
            if (order?.Kind != TurretOperation.Reload || !CanReceive) return;
            int orderId = order.Id;
            var shell = ReloadShellType(order);
            loaded = ReceiveNearbyMortarAmmo(shell, Math.Min(20 - Shells, Needed(shell)),
                physical => Receive(physical, orderId)) > 0;
        }
        // A compatibility API may return false for a full magazine as well as
        // missing ammo; only Core can distinguish its own empty-stock retry.
        nextMortarReloadTick = Find.TickManager.TicksGame + (loaded || CombatCompatibility.Provider != null ? 60 : 300);
    }

    /// <summary>
    /// Supplies real unreserved stacks to the current combat API. Counts are
    /// physical Thing units, not CE magazine charges. The receiver returns the
    /// number of supplied units it accepted and must not generate ammunition.
    /// </summary>
    public int ReceiveNearbyMortarAmmo(ThingDef ammunition, int count, Func<Thing, int> receive)
    {
        if (ammunition == null || count <= 0 || receive == null || !CanMortarAutoload
            || !TryGetFloor(this, out int floor) || !FloorCompatibility.TryEnter(Map, floor, out var scope)) return 0;
        Map map = Map;
        CellRect footprint = this.OccupiedRect();
        float radius = Math.Min(40f, SpecialistModuleExtension.Factor(mortarSupport.autoReloadRadius, 6f));
        int reach = Mathf.CeilToInt(radius), received = 0;
        CellRect area = footprint.ExpandedBy(reach).ClipInsideMap(map);
        using (scope)
        for (int z = area.minZ; z <= area.maxZ; z++)
        for (int x = area.minX; x <= area.maxX; x++)
        {
            var cell = new IntVec3(x, 0, z);
            if (!MortarAmmoInRange(cell, footprint, radius)) continue;
            var things = cell.GetThingList(map);
            for (int i = 0; i < things.Count; i++)
            {
                Thing source = things[i];
                if (!AvailableMortarAmmo(source, ammunition, map, footprint, radius)) continue;
                int take = Math.Min(count - received, source.stackCount);
                // Revalidate immediately before splitting, since optional
                // storage mods may route an item through their own container.
                if (take <= 0 || !AvailableMortarAmmo(source, ammunition, map, footprint, radius)) continue;
                IntVec3 originalCell = source.Position;
                Thing physical = source.SplitOff(take);
                try
                {
                    if (physical != null && !physical.Destroyed && !physical.Spawned && physical.holdingOwner == null
                        && physical.def == ammunition && physical.stackCount > 0 && physical.stackCount <= take
                        && FloorCompatibility.TrySetFloor(physical, floor))
                        received += Math.Clamp(receive(physical), 0, take);
                }
                finally
                {
                    // A rejected transfer or an API which used only part of a
                    // stack must return the same remaining physical objects.
                    RestoreMortarAmmo(physical, originalCell, map, floor);
                }
                if (received >= count || !CanMortarAutoload) return received;
                if (!source.Spawned || source.Destroyed) i--;
            }
        }
        return received;
    }

    private bool AvailableMortarAmmo(Thing thing, ThingDef ammunition, Map map, CellRect footprint, float radius)
        => thing != null && thing.def == ammunition && thing.Spawned && !thing.Destroyed
            && thing.Map == map && thing.stackCount > 0 && !thing.IsForbidden(Faction.OfPlayer)
            && !thing.IsBurning() && SameFloor(this, thing)
            && MortarAmmoInRange(thing.Position, footprint, radius)
            && !map.reservationManager.IsReserved(thing)
            && !map.physicalInteractionReservationManager.IsReserved(thing);

    private static bool MortarAmmoInRange(IntVec3 cell, CellRect footprint, float radius)
    {
        int dx = Math.Max(0, Math.Max(footprint.minX - cell.x, cell.x - footprint.maxX));
        int dz = Math.Max(0, Math.Max(footprint.minZ - cell.z, cell.z - footprint.maxZ));
        return dx * dx + dz * dz <= radius * radius;
    }

    private void RestoreMortarAmmo(Thing physical, IntVec3 cell, Map map, int floor)
    {
        if (physical == null || physical.Destroyed || physical.Spawned || physical.holdingOwner != null) return;
        if (FloorCompatibility.TrySetFloor(physical, floor)
            && (GenPlace.TryPlaceThing(physical, cell, map, ThingPlaceMode.Direct)
                || GenPlace.TryPlaceThing(physical, cell, map, ThingPlaceMode.Near))) return;
        // Keep even an exceptional failed placement as real owned material.
        // Never destroy/recreate it or credit an unaccepted round.
        if (!Contents.TryAdd(physical, false))
            throw new InvalidOperationException("A rejected modular mortar ammunition transfer could not return its physical stack.");
    }
}
