using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using CombatExtended;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace ModularTurrets.Compat.CombatExtended;

public sealed class MountedTravelingShellCE : TravelingShell
{
    public MountedShotSnapshot Snapshot;
    private Thing sourceLauncher;
    private string sourceLauncherLabel="mtCESourceLauncher";
    public override void ExposeData()
    {
        base.ExposeData();Scribe_Deep.Look(ref Snapshot,"mtCEShotSnapshot");
        if(Scribe.mode==LoadSaveMode.Saving)
        {
            sourceLauncher=launcher;sourceLauncherLabel="mtCESourceLauncher";
        }
        else if(Scribe.mode==LoadSaveMode.LoadingVars)
        {
            sourceLauncherLabel="mtCESourceLauncher";
            // Earlier experimental saves have CE's inline launcher{id} entry,
            // but its delayed callback never read that owner-context reference.
            var node=Scribe.loader.curXmlParent;
            string id=node?["loadingId"]?.InnerText;
            if(node?[sourceLauncherLabel]==null&&!string.IsNullOrEmpty(id)&&node?["launcher"+id]!=null)
                sourceLauncherLabel="launcher"+id;
        }
        Scribe_References.Look(ref sourceLauncher,sourceLauncherLabel);
        if(Scribe.mode==LoadSaveMode.PostLoadInit&&launcher==null&&sourceLauncher!=null)launcher=sourceLauncher;
        if(Scribe.mode==LoadSaveMode.PostLoadInit&&Snapshot!=null)Snapshot.MortarShot|=ShotSnapshots.IsMortarWeapon(equipmentDef);
    }
    protected override void Arrived()
    {
        var previous=ShotSnapshots.WorldImpact;ShotSnapshots.WorldImpact=Snapshot;
        try{base.Arrived();}finally{ShotSnapshots.WorldImpact=previous;}
    }
    protected override void LaunchProjectile(IntVec3 sourceCell,LocalTargetInfo target,Map map)
    {
        if(!FloorCompatibility.TryEnter(map,0,out var floor))throw new InvalidOperationException("CE shell arrival floor-0 scope unavailable");
        using(floor)
        {
        var previous=ShotSnapshots.Arriving;ShotSnapshots.Arriving=Snapshot;
        try{base.LaunchProjectile(sourceCell,target,map);}finally{ShotSnapshots.Arriving=previous;}
        }
    }
}
