using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace ModularTurrets;

public sealed class WreckDebris
{
    public ThingDef module;
    public string slot, texture;
    public Vector3 offset;
    public Vector2 size;
    internal Material material;
}
