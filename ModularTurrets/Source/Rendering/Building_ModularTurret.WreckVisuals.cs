using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace ModularTurrets;

public sealed partial class Building_ModularTurret
{
    public override Graphic Graphic => IsWrecked ? WreckVisuals.Resolve(base.Graphic, out _) : base.Graphic;
}
