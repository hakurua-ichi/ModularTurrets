using System;
using System.Collections.Generic;
using Verse;

namespace ModularTurrets.Batteries;

public sealed class BatteryOrder : IExposable
{
    public int Id;
    public BatteryOperation Kind;
    public string Slot;
    public ThingDef PartDef;
    public int PartId;
    public int TargetLevel;
    public List<ThingDefCountClass> Costs = new();
    public float WorkTotal, WorkLeft;
    private int workBalanceVersion = AssemblyWorkBalance.Version;

    public void ExposeData()
    {
        Scribe_Values.Look(ref Id, "id");
        Scribe_Values.Look(ref Kind, "kind");
        Scribe_Values.Look(ref Slot, "slot");
        Scribe_Defs.Look(ref PartDef, "partDef");
        Scribe_Values.Look(ref PartId, "partId");
        Scribe_Values.Look(ref TargetLevel, "targetLevel");
        Scribe_Collections.Look(ref Costs, "costs", LookMode.Deep);
        Scribe_Values.Look(ref WorkTotal, "workTotal");
        Scribe_Values.Look(ref WorkLeft, "workLeft");
        Scribe_Values.Look(ref workBalanceVersion, "workBalanceVersion", 0);
        Costs ??= new();
        if (Scribe.mode == LoadSaveMode.PostLoadInit)
        {
            AssemblyWorkBalance.Migrate(ref workBalanceVersion, ref WorkTotal, ref WorkLeft);
            if (!float.IsFinite(WorkTotal) || WorkTotal <= 0f)
                WorkTotal = Kind switch
                {
                    BatteryOperation.Upgrade => 600f,
                    BatteryOperation.UpgradeModule => Math.Max(1, TargetLevel) * 300f,
                    _ => 100f
                };
            WorkLeft = float.IsFinite(WorkLeft) ? Math.Clamp(WorkLeft, 0f, WorkTotal) : WorkTotal;
        }
    }
}
