using RimWorld;
using Verse;

namespace ModularTurrets;

public sealed partial class Building_ModularTurret
{
    internal bool HasPendingRelocation => Spawned && (Map.listerBuildings?.TryGetReinstallBlueprint(this, out _) == true
        || Map.designationManager?.DesignationOn(this, DesignationDefOf.Uninstall) != null);

    // The backend also handles fresh detached buildings; ownership/selection belong to the UI gate.
    internal bool CanMinifyForRelocation(out string reason)
    {
        reason = null;
        if (!RelocationPatch.Ready || def?.Minifiable != true) reason = L.T("MT_RelocateUnavailable");
        else if (Destroyed || IsWrecked) reason = L.T("MT_RelocateWrecked");
        else if (fitted?.Count > 0) reason = L.T("MT_RelocateRemoveParts");
        else if (Orders?.Count > 0 || blockedReturnOrderId != 0) reason = L.T("MT_RelocateClearOrders");
        else if (contents?.Count > 0 || deferredWreckMaterials?.Contents.Count > 0 || Shells != 0 || LoadedMortarRounds > 0)
            reason = L.T("MT_RelocateClearStock");
        return reason == null;
    }

    internal bool CanRelocate(out string reason)
    {
        if (!CanMinifyForRelocation(out reason)) return false;
        if (!Spawned || Faction != Faction.OfPlayer) reason = L.T("MT_RelocateOwnedSpawned");
        else if (!FloorCompatibility.TryGetFloor(this, out int floor)
            || !FloorCompatibility.TryGetViewFloor(Map, out int view) || floor != view)
            reason = L.T("MT_RelocateCurrentFloor");
        return reason == null;
    }

    internal Command_Action ReinstallGizmo()
    {
        var command = new Command_Action
        {
            defaultLabel = L.T("MT_RelocateLabel"),
            defaultDesc = L.T("MT_RelocateDesc"),
            icon = TexCommand.Install,
            action = () =>
            {
                if (!CanRelocate(out string reason))
                {
                    Messages.Message(reason, this, MessageTypeDefOf.RejectInput, false);
                    return;
                }
                if (Find.Selector.SingleSelectedThing != this)
                {
                    Messages.Message(L.T("MT_RelocateSelectOne"), this, MessageTypeDefOf.RejectInput, false);
                    return;
                }
                Find.DesignatorManager.Select(InstallationDesignatorDatabase.DesignatorFor(def));
            }
        };
        if (!CanRelocate(out string blocked)) command.Disable(blocked);
        else if (Find.Selector.SingleSelectedThing != this) command.Disable(L.T("MT_RelocateSelectOne"));
        return command;
    }
}
