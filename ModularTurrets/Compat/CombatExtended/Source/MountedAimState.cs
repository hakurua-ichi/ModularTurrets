using System;
using System.Linq;
using CombatExtended;
using RimWorld;
using UnityEngine;
using Verse;

namespace ModularTurrets.Compat.CombatExtended;

public sealed class MountedAimState : IExposable
{
    public bool Pending,Ready;
    public int Ticks;
    internal bool Begin(bool shouldAim,float distance,float warmupFactor=1f)
    {
        if(Ready){Ready=false;return false;}
        if(!shouldAim)return false;
        if(!Pending){Pending=true;Ticks=Math.Max(1,(int)(Mathf.Lerp(30f,240f,distance/100f)*warmupFactor));}
        return true;
    }
    internal bool Tick(Action complete)
    {
        if(!Pending)return false;
        if(--Ticks<=0){Pending=false;Ready=true;complete();return false;}
        return true;
    }
    internal void Reset(){Pending=Ready=false;Ticks=0;}
    public void ExposeData()
    {Scribe_Values.Look(ref Pending,"pending");Scribe_Values.Look(ref Ready,"ready");Scribe_Values.Look(ref Ticks,"ticks");}
}
