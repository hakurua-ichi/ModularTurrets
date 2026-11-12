using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace ModularTurrets;

public sealed class TurretOrder : IExposable
{
    public int Id;
    public TurretOperation Kind;
    public string Slot;
    public ThingDef PartDef;
    public int PartId;
    public bool RemovalRolled, RemovalFailed;
    public bool TargetParagon;
    public int TargetTier;
    // Cumulative physical shells already moved into stock, including spent rounds.
    public int ReloadedShells;
    public float WorkTotal;
    public float WorkLeft;
    private int workBalanceVersion = AssemblyWorkBalance.Version;
    public List<ThingDefCountClass> Costs = new();
    public void ExposeData()
    {
        Scribe_Values.Look(ref Id, "id");
        Scribe_Values.Look(ref Kind, "kind");
        Scribe_Values.Look(ref Slot, "slot");
        Scribe_Defs.Look(ref PartDef, "partDef");
        Scribe_Values.Look(ref PartId, "partId");
        Scribe_Values.Look(ref RemovalRolled, "removalRolled");
        Scribe_Values.Look(ref RemovalFailed, "removalFailed");
        Scribe_Values.Look(ref TargetTier, "targetTier");
        Scribe_Values.Look(ref TargetParagon, "targetParagon", false);
        Scribe_Values.Look(ref WorkTotal, "workTotal");
        Scribe_Values.Look(ref WorkLeft, "workLeft");
        Scribe_Values.Look(ref workBalanceVersion, "workBalanceVersion", 0);
        Scribe_Values.Look(ref ReloadedShells, "reloadedShells", 0);
        Scribe_Collections.Look(ref Costs, "costs", LookMode.Deep);
        Costs ??= new();
        if (Scribe.mode == LoadSaveMode.PostLoadInit)
        {
            AssemblyWorkBalance.Migrate(ref workBalanceVersion, ref WorkTotal, ref WorkLeft);
            if (Kind == TurretOperation.Reload) WorkTotal = WorkLeft = 0f;
        }
    }
}
