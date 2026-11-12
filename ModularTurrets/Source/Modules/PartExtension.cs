using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace ModularTurrets;

public sealed class PartExtension : DefModExtension
{
    public bool isHead;
    public float restorationDiscount;
    public bool canUpgrade = true;
    public ThingDef gunDef;
    public List<MountPoint> mounts = new();
    // Range, Shield, Auxiliary, Aiming, Cooling, Servo, FireControl
    public string moduleKind;
    public List<ModuleEffectDef> effects = new();
    public int atlasIndex;
    public float power = 100f;
    public float headSize = 1.8f;
    public Vector2 graphicPivot = new(-1f, -1f);
    public List<PartAnchor> anchors = new();
    public bool usesShells;
    public bool tesla;
    public bool instant;
    public float cooldown = 1f;

    public override IEnumerable<string> ConfigErrors()
    {
        foreach (string error in base.ConfigErrors()) yield return error;
        if (anchors == null) yield break;
        foreach (PartAnchor anchor in anchors)
        {
            if (anchor.beam != null)
                foreach (string error in anchor.beam.ConfigErrors()) yield return anchor.slot + ": " + error;
            if (anchor.muzzles != null)
                foreach (Vector2 muzzle in anchor.muzzles)
                    if (!float.IsFinite(muzzle.x) || !float.IsFinite(muzzle.y)) yield return anchor.slot + ": " + L.T("MT_ConfigMuzzle");
        }
    }
}
