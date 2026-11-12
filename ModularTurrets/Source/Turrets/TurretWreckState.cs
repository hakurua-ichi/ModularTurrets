using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace ModularTurrets;

public sealed partial class Building_ModularTurret
{
    private bool isWrecked;
    private DeferredWreckMaterials deferredWreckMaterials;
    public bool IsWrecked => isWrecked;
    private static TurretWreckStateDef WreckSettings
        => DefDatabase<TurretWreckStateDef>.GetNamedSilentFail("MT_TurretWreckState");
    public int WreckHitPointsBonus => Math.Max(1, WreckSettings?.additionalHitPoints ?? 1000);
    private static float WreckRestorationWork
        => float.IsFinite(WreckSettings?.restorationWork ?? 2500f)
            ? Math.Max(1f, WreckSettings?.restorationWork ?? 2500f) : 2500f;

    // Intercept before ThingWithComps.Kill sends death notifications. A second
    // lethal hit follows the original destruction path and destroys contents.
    public override void Kill(DamageInfo? dinfo = null, Hediff exactCulprit = null)
    {
        if (TryEnterWreckedState()) return;
        base.Kill(dinfo, exactCulprit);
    }

    private void ExplodeAfterDestruction(Map map, IntVec3 cell, int floor)
    {
        if (map == null || !Destroyed || !cell.InBounds(map) || !FloorCompatibility.TryEnter(map, floor, out var scope)) return;
        using (scope)
        {
            // Vanilla circular blast within a five-by-five bounding area. Run
            // after removal so the explosion cannot kill its own turret again.
            GenExplosion.DoExplosion(cell, map, 2.4f, DamageDefOf.Bomb, this, damAmount: 50, weapon: def);
        }
    }

    private bool TryEnterWreckedState()
    {
        if (!Spawned || Destroyed || isWrecked || fitted.Count == 0) return false;
        isWrecked = true;
        RestoreDurabilityForStage();
        StopWreckedSystems();
        UpdatePower();
        return true;
    }

    private void StopWreckedSystems()
    {
        targetCoordination?.ReleaseAll();
        forcedTarget = LocalTargetInfo.Invalid;
        shieldEnergy = 0f;
        shieldDelay = 0;
        foreach (var channel in channels)
        {
            channel.Verb?.Reset();
            channel.Target = LocalTargetInfo.Invalid;
            channel.Warmup = 0;
            channel.Cooldown = Math.Max(60, channel.Cooldown);
            channel.QuickRetarget = false;
            channel.OpeningShotReady = false;
        }
        pendingFlameTiles.Clear();
        beams.Clear();
        if (teslaPulses != null) foreach (var pulse in teslaPulses) pulse.Active = false;
    }

    private void ExposeWreckState()
    {
        Scribe_Values.Look(ref isWrecked, "modularWrecked", false);
        Scribe_Deep.Look(ref deferredWreckMaterials, "modularDeferredWreckMaterials", this);
    }

    public bool QueueWreckRepair()
    {
        if (!isWrecked || Orders.Any(o => o.Kind == TurretOperation.Restore)) return false;
        var order = new TurretOrder { Kind = TurretOperation.Restore, WorkTotal = WreckRestorationWork, Costs = GetRestorationCosts() };
        if (!AddOrder(order)) return false;
        var deferred = (deferredWreckMaterials ??= new DeferredWreckMaterials(this)).Contents;
        foreach (var thing in Staged.ToList()) deferred.TryAddOrTransfer(thing);
        // Retain all existing materials, upgrades and progress behind restoration.
        Orders.Remove(order);
        Orders.Insert(0, order);
        return true;
    }

    private void CompleteWreckRepair()
    {
        isWrecked = false;
        RestoreDurabilityForStage();
        // Power returns through RebuildChannels after this order is removed.
        // Ordinary HP repair alone never releases the disabled-state latch.
    }

    private void RestoreDeferredWreckMaterials()
    {
        if (deferredWreckMaterials == null) return;
        foreach (var thing in deferredWreckMaterials.Contents.ToList()) Contents.TryAddOrTransfer(thing);
    }

    private sealed class DeferredWreckMaterials : IThingHolder, IExposable
    {
        private ThingOwner<Thing> contents;
        public IThingHolder ParentHolder { get; }
        public ThingOwner<Thing> Contents => contents ??= new ThingOwner<Thing>(this);
        public DeferredWreckMaterials(IThingHolder parent) { ParentHolder = parent; }
        public ThingOwner GetDirectlyHeldThings() => Contents;
        public void GetChildHolders(List<IThingHolder> children) => ThingOwnerUtility.AppendThingHoldersFromThings(children, Contents);
        public void ExposeData() => Scribe_Deep.Look(ref contents, "contents", this);
    }

    private static Texture2D wreckRepairIcon;

    private Command_Action WreckRepairCommand()
    {
        var repair = Orders.FirstOrDefault(o => o.Kind == TurretOperation.Restore);
        var costs = repair?.Costs ?? GetRestorationCosts();
        string description = L.T("MT_WreckRestoreDesc", WreckRestorationWork) + "\n\n"
            + (costs.Count == 0 ? L.T("MT_WreckRestoreNoCost") : L.T("MT_WreckRestoreCosts") + "\n"
                + string.Join("\n", costs.Select(c => L.Label(c.thingDef) + " × " + c.count.ToString("N0"))));
        if (repair == null && RestorationDiscount > 0f)
            description += "\n" + L.T("MT_WreckRestoreDiscount", RestorationDiscount.ToString("P0"));
        if (RestorationCostsEstimated) description += "\n" + L.T("MT_WreckRestoreEstimated");
        var command = new Command_Action
        {
            defaultLabel = L.T("MT_WreckRestoreLabel"),
            defaultDesc = description,
            icon = wreckRepairIcon ??= ContentFinder<Texture2D>.Get("ModularTurrets/UI/Repair"),
            action = () => QueueWreckRepair()
        };
        if (repair != null) command.Disable(L.T("MT_WreckRestoreQueued"));
        return command;
    }
}
