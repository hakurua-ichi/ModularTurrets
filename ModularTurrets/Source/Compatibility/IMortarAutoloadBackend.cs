using System;
using System.Collections.Generic;
using Verse;

namespace ModularTurrets;

// Optional combat APIs keep their actual ammunition and magazine semantics.
public interface IMortarAutoloadBackend
{
    bool TryAutoReload(Building_ModularTurret owner, FireChannel channel);
}
