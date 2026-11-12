using System;
using System.Collections.Generic;
using Verse;

namespace ModularTurrets;

public sealed partial class CompTurretPart
{
    public override string GetDescriptionPart()
        => Building_ModularTurret.ContextualPartEffects(parent, TurretPartContext.OwnerOf(parent));
}
