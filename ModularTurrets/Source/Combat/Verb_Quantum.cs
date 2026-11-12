using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace ModularTurrets;

public sealed class Verb_Quantum : Verb_Instant
{
    protected override bool IgnoresObstacles => true;
    protected override void ShowShot(Building_ModularTurret turret, Vector3 end, bool hit)
    {
        // A visual pop on the single target, without a travelling projectile or blast damage.
        if (hit) FleckMaker.Static(end, turret.Map, FleckDefOf.ExplosionFlash, 0.35f);
    }
}
