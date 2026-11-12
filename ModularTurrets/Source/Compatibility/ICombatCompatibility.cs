using System;
using System.Collections.Generic;
using Verse;

namespace ModularTurrets;

public interface ICombatCompatibility
{
    void PrepareChannel(Building_ModularTurret owner, FireChannel channel);
    void ConfigureChannel(Building_ModularTurret owner, FireChannel channel);
    void TickChannel(Building_ModularTurret owner, FireChannel channel);
}
