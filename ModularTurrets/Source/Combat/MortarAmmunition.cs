using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using RimWorld;
using Verse;

namespace ModularTurrets;

public sealed partial class Building_ModularTurret
{
    public ThingDef SelectedShellDef, LoadedShellDef;
    private static ThingDef DefaultMortarShell => DefDatabase<ThingDef>.GetNamed("Shell_HighExplosive");
    private IMortarAmmoManagementBackend MortarAmmoBackend => CombatCompatibility.Provider as IMortarAmmoManagementBackend;
    private ThingDef CoreLoadedMortarAmmo => Shells > 0 ? LoadedShellDef ?? DefaultMortarShell : null;
    private ThingDef CoreSelectedMortarAmmo => SelectedShellDef ?? CoreLoadedMortarAmmo ?? DefaultMortarShell;
    public IEnumerable<ThingDef> MortarAmmoTypes => CombatCompatibility.Provider != null
        ? MortarAmmoBackend?.MortarAmmoTypes(this) ?? Enumerable.Empty<ThingDef>() : MortarShellProjectiles.Shells;
    public ThingDef SelectedMortarAmmo => CombatCompatibility.Provider != null
        ? MortarAmmoBackend?.SelectedMortarAmmo(this) : CoreSelectedMortarAmmo;
    public ThingDef LoadedMortarAmmo => CombatCompatibility.Provider != null
        ? MortarAmmoBackend?.LoadedMortarAmmo(this) : CoreLoadedMortarAmmo;
    public int LoadedMortarRounds => CombatCompatibility.Provider != null
        ? MortarAmmoBackend?.LoadedMortarRounds(this) ?? 0 : Shells;
    public ThingDef CoreMortarProjectile => MortarShellProjectiles.ProjectileFor(CoreLoadedMortarAmmo ?? CoreSelectedMortarAmmo, RapidCoreMortarProjectiles);
    public bool CanUnloadMortarAmmo => CombatCompatibility.Provider != null
        ? MortarAmmoBackend?.CanUnloadMortarAmmo(this) == true
        : Spawned && Faction == Faction.OfPlayer && (Shells > 0 || Orders.Any(o => o.Kind == TurretOperation.Reload)
            || Staged.Any(t => MortarShellProjectiles.IsShellResource(t.def)));

    public bool TrySelectMortarAmmo(ThingDef ammunition)
    {
        if (CombatCompatibility.Provider != null) return MortarAmmoBackend?.TrySelectMortarAmmo(this, ammunition) == true;
        if (!Spawned || Faction != Faction.OfPlayer || Head?.usesShells != true || !MortarShellProjectiles.Supports(ammunition)) return false;
        if (CoreSelectedMortarAmmo == ammunition) return true;
        SelectedShellDef = ammunition;
        // The ledger credits only already consumed physical rounds. Canceling
        // the remaining request does not refund or convert any of those rounds.
        Orders.RemoveAll(o => o.Kind == TurretOperation.Reload);
        nextMortarReloadTick = 0;
        return true;
    }

    public bool TryUnloadMortarAmmo()
    {
        if (CombatCompatibility.Provider != null) return MortarAmmoBackend?.TryUnloadMortarAmmo(this) == true;
        if (!CanUnloadMortarAmmo || !TryGetFloor(this, out int floor)
            || !FloorCompatibility.TryEnter(Map, floor, out var scope)) return false;
        using (scope)
        {
            if (!MaterializeMortarStock()) return false;
            AutoReload = false;
            Orders.RemoveAll(o => o.Kind == TurretOperation.Reload);
            bool returned = true;
            foreach (var shell in Staged.Where(t => MortarShellProjectiles.IsShellResource(t.def)).ToList())
            {
                if (!FloorCompatibility.TrySetFloor(shell, floor)
                    || !Contents.TryDrop(shell, Position, Map, ThingPlaceMode.Near, out _)) returned = false;
            }
            // Failed drops remain real, serialized contents and keep the unload
            // action available. Neither stock nor spent-round ledger is rebuilt.
            return returned;
        }
    }

    private bool MaterializeMortarStock()
    {
        if (Shells <= 0) { LoadedShellDef = null; return true; }
        ThingDef shellDef = CoreLoadedMortarAmmo;
        var physical = ThingMaker.MakeThing(shellDef);
        physical.stackCount = Shells;
        if (!Contents.TryAdd(physical, false)) { physical.Destroy(); return false; }
        Shells = 0; LoadedShellDef = null;
        return true;
    }

    private void ExposeMortarAmmunition()
    {
        Scribe_Defs.Look(ref SelectedShellDef, "selectedMortarShell");
        Scribe_Defs.Look(ref LoadedShellDef, "loadedMortarShell");
        if (Scribe.mode != LoadSaveMode.PostLoadInit) return;
        NormalizeMortarAmmunition();
    }

    private void NormalizeMortarAmmunition()
    {
        if (Shells > 0) LoadedShellDef ??= DefaultMortarShell;
        else LoadedShellDef = null;
        SelectedShellDef ??= LoadedShellDef ?? DefaultMortarShell;
    }

    private static ThingDef ReloadShellType(TurretOrder order)
        => order?.Kind == TurretOperation.Reload && order.Costs.Count == 1
            && MortarShellProjectiles.IsShellResource(order.Costs[0].thingDef) ? order.Costs[0].thingDef : null;
}
