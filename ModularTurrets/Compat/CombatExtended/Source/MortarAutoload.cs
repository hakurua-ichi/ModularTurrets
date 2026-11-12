using CombatExtended;
using RimWorld;
using Verse;

namespace ModularTurrets.Compat.CombatExtended;

internal sealed partial class CombatAdapter
{
    public bool TryAutoReload(Building_ModularTurret owner, FireChannel channel)
    {
        if (!owner.AutoReload || !owner.Spawned || channel?.Slot != "head" || channel.Verb is not Verb_MountedMortarCE
            || channel.Gun?.TryGetComp<CompAmmoUser>() is not { UseAmmo: true, HasMagazine: true } ammo
            || ammo.turret != owner || ammo.SelectedAmmo is not { } selected
            || ammo.CurMagCount >= ammo.MagSize || ammo.CurMagCount > 0 && ammo.CurrentAmmo != selected
            || GetReloading(owner) || owner.Map.reservationManager.IsReserved(owner)
            || owner.Map.physicalInteractionReservationManager.IsReserved(owner))
            return false;

        var types = ammo.CurAmmoSet?.ammoTypes;
        if (types == null || selected.ammoCount <= 0 || selected.ammoCount > ammo.MagSize) return false;
        foreach (var link in types)
        {
            if (link?.ammo != selected || link.projectile == null) continue;
            return owner.ReceiveNearbyMortarAmmo(selected, 1, item =>
            {
                // CE reloadOneAtATime may convert a single physical item to multiple
                // charges. Refuse it when all of those charges cannot fit.
                int charge = ammo.Props.reloadOneAtATime
                    ? item.TryGetComp<CompApparelReloadable>()?.RemainingCharges ?? selected.ammoCount : 1;
                int before = ammo.CurMagCount;
                if (item.stackCount != 1 || item.def != selected || charge <= 0 || charge > ammo.MagSize - before
                    || !owner.AutoReload || GetReloading(owner) || !ammo.UseAmmo || ammo.SelectedAmmo != selected
                    || before > 0 && ammo.CurrentAmmo != selected) return 0;
                ammo.LoadAmmo(item);
                return item.Destroyed && ammo.CurMagCount > before ? 1 : 0;
            }) > 0;
        }
        return false;
    }
}
