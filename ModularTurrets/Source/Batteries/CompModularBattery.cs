using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace ModularTurrets.Batteries;

// Keep Core's storedPower, power-net accounting, self-discharge and EMP behavior.
public sealed class CompModularBattery : CompPowerBattery
{
    public override float GetStatOffset(StatDef stat)
        => base.GetStatOffset(stat) + ModularMarketValue.StatOffset(parent, stat);

    public override void GetStatsExplanation(StatDef stat, System.Text.StringBuilder text, string whitespace = "")
    {
        base.GetStatsExplanation(stat, text, whitespace);
        ModularMarketValue.Explain(parent, stat, text, whitespace);
    }

    private CompProperties_ModularBattery instanceProps;
    private CompBatteryGeneration generator;
    private PowerNet generationNet;
    private float reserveEnergy;
    private bool checkWirelessLinks;
    internal List<Thing> WirelessReceivers;
    public float BaseCapacity { get; private set; }
    public float BaseEfficiency { get; private set; }
    public float ReserveEnergy => reserveEnergy;
    public float ReserveCapacity { get; private set; }
    public float GenerationWatts { get; private set; }
    public float WirelessRadius { get; private set; }
    public bool ProtectsFromShortCircuit { get; private set; }
    public bool PreventsBreakdown { get; private set; }
    public float TotalCapacity => Props.storedEnergyMax + ReserveCapacity;
    public float ChargeFraction => Math.Clamp((StoredEnergy + reserveEnergy) / TotalCapacity, 0f, 1f);

    private static bool ReadingReferences => Scribe.mode == LoadSaveMode.LoadingVars
        || Scribe.mode == LoadSaveMode.ResolvingCrossRefs;

    public override void Initialize(CompProperties props)
    {
        if (props is not CompProperties_ModularBattery definition)
            throw new ArgumentException("Modular battery requires its own properties.", nameof(props));
        instanceProps = definition.CopyForInstance();
        BaseCapacity = definition.storedEnergyMax;
        BaseEfficiency = definition.efficiency;
        base.Initialize(instanceProps);
        Configure(BaseCapacity, BaseEfficiency);
    }

    public void Configure(float capacity, float efficiency)
    {
        if (!float.IsFinite(capacity) || capacity <= 0f)
            throw new ArgumentOutOfRangeException(nameof(capacity));
        if (!float.IsFinite(efficiency) || efficiency <= 0f)
            throw new ArgumentOutOfRangeException(nameof(efficiency));
        efficiency = Math.Min(efficiency, 1f);
        if (!float.IsFinite(capacity / efficiency))
            throw new ArgumentOutOfRangeException(nameof(capacity));
        if (instanceProps == null) throw new InvalidOperationException("Battery properties are not initialized.");

        instanceProps.efficiency = efficiency;
        // Core clamps storedPower in every Scribe phase. The parent's fitted
        // references do not exist yet in LoadingVars/ResolvingCrossRefs.
        instanceProps.storedEnergyMax = ReadingReferences ? float.MaxValue : capacity;
        if (!ReadingReferences)
        {
            if (!float.IsFinite(StoredEnergy) || StoredEnergy < 0f) SetStoredEnergyPct(0f);
            else if (StoredEnergy > capacity) SetStoredEnergyPct(1f);
        }
    }

    private static float Nonnegative(float value) => float.IsFinite(value) && value > 0f ? value : 0f;

    internal void RefreshFromBuilding(int level, float capacityPerUpgrade, BatteryModuleExtension left,
        BatteryModuleExtension right, int leftLevel = 0, int rightLevel = 0)
    {
        if (ReadingReferences) return;
        float upgradeFactor = 1f + Nonnegative(left?.upgradeCapacityBonusFactor ?? 0f)
            + Nonnegative(right?.upgradeCapacityBonusFactor ?? 0f);
        float capacity = BaseCapacity + Math.Max(0, level) * capacityPerUpgrade * upgradeFactor
            + Nonnegative(left?.additionalCapacityWd ?? 0f) + Nonnegative(right?.additionalCapacityWd ?? 0f);
        float efficiency = Math.Max(BaseEfficiency + Nonnegative(left?.efficiencyBonus ?? 0f)
            + Nonnegative(right?.efficiencyBonus ?? 0f), Math.Max(Nonnegative(left?.chargeEfficiencyFloor ?? 0f),
            Nonnegative(right?.chargeEfficiencyFloor ?? 0f)));
        float displaced = Nonnegative(StoredEnergy - capacity);
        Configure(capacity, efficiency);
        GenerationWatts = (left?.GenerationWattsAtLevel(capacity, leftLevel) ?? 0f)
            + (right?.GenerationWattsAtLevel(capacity, rightLevel) ?? 0f);
        generator ??= parent?.GetComp<CompBatteryGeneration>();
        if (generator != null)
        {
            generator.PowerOutput = GenerationWatts;
            SyncGenerationNet();
            if (parent.Spawned && GenerationWatts > 0f) generator.TryStart();
        }
        ProtectsFromShortCircuit = left?.protectsFromShortCircuit == true || right?.protectsFromShortCircuit == true;
        PreventsBreakdown = left?.preventsBreakdown == true || right?.preventsBreakdown == true;
        ReserveCapacity = capacity * Math.Min(1f, Math.Max(Nonnegative(left?.reserveFraction ?? 0f),
            Nonnegative(right?.reserveFraction ?? 0f)));
        reserveEnergy = Nonnegative(reserveEnergy);
        reserveEnergy += Math.Min(displaced, Math.Max(0f, ReserveCapacity - reserveEnergy));
        if (reserveEnergy > ReserveCapacity)
        {
            // On removal, return charge to usable main storage before discarding overflow.
            TransferFromReserve(reserveEnergy - ReserveCapacity);
            reserveEnergy = Math.Min(reserveEnergy, ReserveCapacity);
        }
        float radius = Math.Max(left?.WirelessRadiusAtLevel(leftLevel) ?? 0f, right?.WirelessRadiusAtLevel(rightLevel) ?? 0f);
        bool hadCoil = WirelessRadius > 0f;
        WirelessRadius = radius;
        if (parent?.Spawned == true && Scribe.mode == LoadSaveMode.Inactive && (hadCoil || radius > 0f))
            BatteryWireless.Refresh(this);
    }

    private void TransferFromReserve(float requested)
    {
        float amount = Math.Min(Math.Min(requested, reserveEnergy), Props.storedEnergyMax - StoredEnergy);
        if (amount <= 0f) return;
        float previous = StoredEnergy;
        SetStoredEnergyPct((previous + amount) / Props.storedEnergyMax);
        reserveEnergy = Math.Max(0f, reserveEnergy - (StoredEnergy - previous));
    }

    private void UpdateReserve()
    {
        PowerNet net = PowerNet;
        if (net == null || StunnedByEMP || parent.IsBrokenDown() || net.Map.gameConditionManager.ElectricityDisabled(net.Map)) return;
        float mainCapacity = Props.storedEnergyMax;
        float chargeForReserve = Math.Min(StoredEnergy - mainCapacity * 0.8f, ReserveCapacity - reserveEnergy);
        if (chargeForReserve > 0f)
        {
            if (net.CurrentEnergyGainRate() >= 0f)
            {
                float previous = StoredEnergy;
                DrawPower(chargeForReserve);
                reserveEnergy = Math.Min(ReserveCapacity, reserveEnergy + (previous - StoredEnergy));
            }
            return;
        }
        if (reserveEnergy <= 0f) return;
        float gain = net.CurrentEnergyGainRate();
        // Cover the next service interval and Core's 5 Wd restart threshold.
        // A recently shed load has zero actual draw, so explicitly consider its restart.
        float buffer = 5.1f + Math.Max(0f, -gain) * 120f;
        if (gain >= 0f)
        {
            float restartDemand = 0f;
            foreach (var consumer in net.powerComps)
                if (!consumer.PowerOn && consumer.EnergyOutputPerTick < 0f
                    && FlickUtility.WantsToBeOn(consumer.parent) && !consumer.parent.IsBrokenDown())
                    restartDemand = Math.Max(restartDemand, -consumer.EnergyOutputPerTick);
            if (restartDemand <= 0f) return;
            buffer += Math.Max(0f, restartDemand - gain) * 120f;
        }
        if (StoredEnergy >= buffer) return;
        float needed = buffer - net.CurrentStoredEnergy();
        if (needed > 0f) TransferFromReserve(needed);
    }

    public override void PostSpawnSetup(bool respawningAfterLoad)
    {
        base.PostSpawnSetup(respawningAfterLoad);
        // Core restores connector references at PostLoadInit. Validate after that phase.
        checkWirelessLinks = respawningAfterLoad;
    }

    public override void PostSwapMap()
    {
        base.PostSwapMap();
        BatteryWireless.RestoreRemoteLinks(this);
    }

    public override void CompTick()
    {
        base.CompTick();
        if (generationNet != transNet) SyncGenerationNet();
        // Core breakdown checks the trader's on state even for a plain battery.
        // Re-enable after repairs when the dormant trader is outside the native tick list.
        if (generator != null && !generator.PowerOn && parent.IsHashIntervalTick(60)) generator.TryStart();
        if (ReserveCapacity > 0f && parent.IsHashIntervalTick(60)) UpdateReserve();
        if (checkWirelessLinks || WirelessRadius > 0f && parent.IsHashIntervalTick(250))
        {
            checkWirelessLinks = false;
            BatteryWireless.Refresh(this);
        }
    }

    private void SyncGenerationNet()
    {
        generationNet = transNet;
        if (generationNet == null || generator == null) return;
        // Core registers both comps on net creation. Empty slots should not add
        // a permanent zero-output trader to the network's per-tick dispatch.
        if (GenerationWatts <= 0f) generationNet.powerComps.Remove(generator);
        else if (!generationNet.powerComps.Contains(generator)) generationNet.powerComps.Add(generator);
    }

    public override void ReceiveCompSignal(string signal)
    {
        base.ReceiveCompSignal(signal);
        if (signal == CompBreakdownable.BreakdownSignal)
        {
            reserveEnergy = 0f;
            if (WirelessRadius > 0f && parent.Spawned) BatteryWireless.Refresh(this);
        }
    }

    public override void PostDeSpawn(Map map, DestroyMode mode = DestroyMode.Vanish)
    {
        BatteryWireless.ReleaseRemoteLinks(this);
        generationNet = null;
        base.PostDeSpawn(map, mode);
    }

    public override void PostExposeData()
    {
        Scribe_Values.Look(ref reserveEnergy, "batteryReserveEnergy");
        Scribe_Collections.Look(ref WirelessReceivers, "batteryWirelessReceivers", LookMode.Reference);
        if (ReadingReferences) instanceProps.storedEnergyMax = float.MaxValue;
        else if (Scribe.mode == LoadSaveMode.PostLoadInit && parent is Building_ModularBattery battery)
            battery.RefreshBatteryPower();
        base.PostExposeData();
    }
}
