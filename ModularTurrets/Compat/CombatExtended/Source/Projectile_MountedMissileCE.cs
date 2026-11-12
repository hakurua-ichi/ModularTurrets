using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CombatExtended;
using CombatExtended.Lasers;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace ModularTurrets.Compat.CombatExtended;

public sealed class Projectile_MountedMissileCE : ProjectileCE_Explosive
{
    private Vector3 bend;
    private float ordinaryFlightTicks;
    private int smokeInterval=2;
    private float smokeSize=0.35f;
    private bool curveInitialized;
    public override void Launch(Thing launcher,Vector2 origin,float shotAngle,float shotRotation,float shotHeight=0f,float shotSpeed=-1f,Thing equipment=null,float distance=-1f)
    {
        curveInitialized=false;
        base.Launch(launcher,origin,shotAngle,shotRotation,shotHeight,shotSpeed,equipment,distance);
        var settings=def.GetModExtension<MissileFlightExtension>()??throw new InvalidOperationException("MT CE missile flight extension missing.");
        if(launcher is not Building_ModularTurret owner)throw new InvalidOperationException("MT CE missile requires its mounted owner.");
        Vector3 from=new(origin.x,0f,origin.y),to=new(Destination.x,0f,Destination.y),direction=to-from;
        float length=direction.magnitude,width=Math.Min(settings.curveWidth,length*settings.distanceWidthLimit);
        int lane=SpecialWeapons.NextLane(owner);
        bend=length>0.001f?new Vector3(-direction.z,0f,direction.x)/length
            *(width*((lane&1)==0?-1f:1f)*(0.65f+0.35f*(lane/2)/3f)*Rand.Range(0.92f,1f)):Vector3.zero;
        bend=LimitBend(from,to,bend,Map.Size.x,Map.Size.z);
        ordinaryFlightTicks=startingTicksToImpact;
        startingTicksToImpact=Math.Max(1f,startingTicksToImpact*settings.durationMultiplier);
        ticksToImpact=Mathf.CeilToInt(startingTicksToImpact);
        smokeInterval=Math.Max(1,settings.smokeInterval);smokeSize=Math.Max(0f,settings.smokeSize);
        curveInitialized=true;SpecialWeapons.Missiles++;
    }
    internal Vector3 PositionAt(int tick)
    {
        if(!curveInitialized)throw new InvalidOperationException("MT CE curve used before initialization or from an unsupported saved projectile.");
        float t=Mathf.Clamp01(tick/startingTicksToImpact),seconds=t*ordinaryFlightTicks/60f;
        var position=Vector2.Lerp(origin,Destination,t);
        var curve=bend*(6.75f*t*(1f-t)*(1f-t));
        float height=shotHeight+shotSpeed*Mathf.Sin(shotAngle)*seconds-GravityPerWidth*seconds*seconds/2f;
        return new Vector3(position.x+curve.x,height,position.y+curve.z);
    }
    public override void Tick()
    {
        Vector3 previous=ExactPosition;
        base.Tick();
        if(Spawned&&!Destroyed&&!landed&&smokeSize>0f&&FlightTicks%smokeInterval==0&&previous.InBounds(Map))
            FleckMaker.ThrowSmoke(previous,Map,smokeSize);
    }
    public override void ExposeData()
    {
        base.ExposeData();Scribe_Values.Look(ref bend,"mtCECurveBend");Scribe_Values.Look(ref ordinaryFlightTicks,"mtCEOrdinaryFlightTicks");
        Scribe_Values.Look(ref curveInitialized,"mtCECurveInitialized");Scribe_Values.Look(ref smokeInterval,"mtCESmokeInterval",2);
        Scribe_Values.Look(ref smokeSize,"mtCESmokeSize",0.35f);
    }
    private static Vector3 LimitBend(Vector3 from,Vector3 to,Vector3 offset,int width,int height)
    {
        if(from.x<0f||from.z<0f||from.x>=width||from.z>=height||to.x<0f||to.z<0f||to.x>=width||to.z>=height)return Vector3.zero;
        Vector3 control=from+(to-from)/3f,delta=offset*2.25f;float scale=1f;
        if(delta.x>0f)scale=Math.Min(scale,(width-0.001f-control.x)/delta.x);
        else if(delta.x<0f)scale=Math.Min(scale,(0.001f-control.x)/delta.x);
        if(delta.z>0f)scale=Math.Min(scale,(height-0.001f-control.z)/delta.z);
        else if(delta.z<0f)scale=Math.Min(scale,(0.001f-control.z)/delta.z);
        return offset*Mathf.Clamp01(scale);
    }
}
