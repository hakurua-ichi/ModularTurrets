using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace ModularTurrets;

public sealed class MountPoint
{
    public string id;
    public Vector3 offset;
    public float size = 0.65f;
    public List<ModuleMountOverride> moduleOverrides = new();
}
