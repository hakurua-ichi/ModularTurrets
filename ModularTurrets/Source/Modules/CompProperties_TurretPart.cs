using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace ModularTurrets;

public sealed class CompProperties_TurretPart : CompProperties
{
    public CompProperties_TurretPart() => compClass = typeof(CompTurretPart);
}
