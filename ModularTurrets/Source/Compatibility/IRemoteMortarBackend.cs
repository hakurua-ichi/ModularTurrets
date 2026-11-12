using System;
using System.Collections.Generic;
using Verse;

namespace ModularTurrets;

public interface IRemoteMortarBackend
{
    bool Order(Building_ModularTurret owner, Map destination, IntVec3 cell, out string reason);
    bool HasMission(Building_ModularTurret owner);
    LocalTargetInfo LocalAim(Building_ModularTurret owner);
    void Cancel(Building_ModularTurret owner);
    IEnumerable<Gizmo> GetGizmos(Building_ModularTurret owner);
}
