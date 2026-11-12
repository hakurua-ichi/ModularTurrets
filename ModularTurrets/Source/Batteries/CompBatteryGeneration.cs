using System.Collections.Generic;
using RimWorld;
using Verse;

namespace ModularTurrets.Batteries;

// PowerNet registers both a transmitter's battery and its trader. The battery owns
// the physical connection; this second comp only supplies watts to the native ledger.
public sealed class CompBatteryGeneration : CompPowerTrader
{
    public override void PostSpawnSetup(bool respawningAfterLoad)
    {
        base.PostSpawnSetup(respawningAfterLoad);
        PowerOutput = parent.GetComp<CompModularBattery>()?.GenerationWatts ?? 0f;
        TryStart();
    }

    internal void TryStart()
    {
        if (!PowerOn && !parent.IsBrokenDown() && FlickUtility.WantsToBeOn(parent)) PowerOn = true;
    }

    // This secondary component has no separate physical connection to lose on map swap.
    public override void PostSwapMap() => TryStart();

    // Output is derived from fitted modules. Only the primary battery scribes parentThing.
    public override void PostExposeData() { }
    public override void CompPrintForPowerGrid(SectionLayer layer) { }
    public override string CompInspectStringExtra() => null;
    public override IEnumerable<Gizmo> CompGetGizmosExtra() { yield break; }
}
