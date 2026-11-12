using System;
using System.Collections.Generic;
using Verse;

namespace ModularTurrets;

public interface IMortarAmmoManagementBackend
{
    IEnumerable<ThingDef> MortarAmmoTypes(Building_ModularTurret owner);
    ThingDef SelectedMortarAmmo(Building_ModularTurret owner);
    ThingDef LoadedMortarAmmo(Building_ModularTurret owner);
    int LoadedMortarRounds(Building_ModularTurret owner);
    bool TrySelectMortarAmmo(Building_ModularTurret owner, ThingDef ammunition);
    bool CanUnloadMortarAmmo(Building_ModularTurret owner);
    bool TryUnloadMortarAmmo(Building_ModularTurret owner);
}
