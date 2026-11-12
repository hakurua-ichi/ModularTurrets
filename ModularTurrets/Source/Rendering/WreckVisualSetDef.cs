using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace ModularTurrets;

public sealed class WreckVisualSetDef : Def
{
    public List<WreckTextureReplacement> replacements = new();
    public List<WreckDebris> debris = new();
}
