using System;
using System.Collections.Generic;
using Verse;

namespace ModularTurrets;

public sealed partial class Building_ModularTurret
{
    internal static string ContextualPartEffects(Thing part, Building_ModularTurret owner)
        => AssemblyWindow.PartEffectText(part, part.TryGetComp<CompTurretPart>()?.Tier ?? 0, context: owner);
}
