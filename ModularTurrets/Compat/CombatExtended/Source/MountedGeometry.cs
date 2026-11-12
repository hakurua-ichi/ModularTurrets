using System;
using CombatExtended;
using UnityEngine;
using Verse;

namespace ModularTurrets.Compat.CombatExtended;

internal static class MountedGeometry
{
    internal static float Height(Thing caster, float ordinary)
    {
        if(caster is not Building_ModularTurret owner||owner.HeadPart==null)return ordinary;
        var extension=owner.def.GetModExtension<MountedHeightExtension>();
        return Math.Max(ordinary,extension?.shotHeight ?? 2.6f);
    }
    internal static Vector3 Origin(Thing caster, Thing equipment, Vector3 ordinary, float height)
    {
        Vector3 p=caster is Building_ModularTurret owner?owner.MuzzleFor(equipment):ordinary;
        p.y=height;return p;
    }
    internal static float Accuracy(Thing equipment)
    {
        float factor=equipment?.TryGetComp<CompTurretWeaponAccuracy>()?.MissFactor??1f;
        return float.IsFinite(factor)?Math.Max(0f,factor):1f;
    }

    internal static bool SingleAdjacentSolid(Building_ModularTurret owner,Vector3 source,IntVec3 target,Thing targetThing)
    {
        var map=owner.Map;
        var footprint=owner.OccupiedRect();var adjacent=footprint.ExpandedBy(1);
        IntVec3 firstExternal=IntVec3.Invalid;
        foreach(var point in GenSightCE.PointsOnLineOfSight(source,target.ToVector3Shifted()))
        {
            // CE's iterator retains source height in cell.y; map cells use y0.
            var cell=point;cell.y=0;
            if(footprint.Contains(cell))continue;
            if(!firstExternal.IsValid)firstExternal=cell;
            if(cell==target)continue;
            if(!cell.InBounds(map))return false;
            var solid=cell.GetEdifice(map);
            if(solid==null||solid==owner||solid==targetThing||solid.def.Fillage!=FillCategory.Full
                || new CollisionVertical(solid).Max<=0f)continue;
            // One immediately adjacent occupied cell, not one arbitrarily wide
            // Thing or any other wall farther along the elevated firing line.
            if(cell!=firstExternal||!adjacent.Contains(cell))return false;
        }
        return true;
    }

    internal static Thing CoverWithoutCaster(Thing caster,LocalTargetInfo target,bool instant)
    {
        var map=caster.Map;
        var cells=GenSightCE.AllPointsOnLineOfSight(target.Cell,caster.Position);
        if(cells.Count<3)return null;
        Thing highest=null;float height=0f;
        int end=instant?cells.Count:cells.Count/2;
        for(int i=0;i<end;i++)
        {
            var cell=cells[i];
            if(!cell.InBounds(map)||cell.AdjacentTo8Way(caster.Position))continue;
            if(!instant&&i>cells.Count/2)continue;
            Thing candidate=cell.GetFirstPawn(map)??cell.GetCover(map);
            if(candidate==null||candidate==caster||candidate==target.Thing||candidate.def.Fillage!=FillCategory.Partial
                || candidate.IsPlant()||candidate is RimWorld.Building_TrapExplosive)continue;
            float next=new CollisionVertical(candidate).Max;
            if(highest==null||height<next){highest=candidate;height=next;}
        }
        return highest;
    }
}
