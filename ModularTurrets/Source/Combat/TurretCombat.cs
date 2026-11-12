using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace ModularTurrets;

public sealed partial class Building_ModularTurret
{
    private List<FireChannel> channels = new();
    public IReadOnlyList<FireChannel> CombatChannels => channels;
    public override Verb AttackVerb => CombatCompatibility.IsAvailable ? channels.FirstOrDefault(c => c.Slot == "head")?.Verb : null;
    public override LocalTargetInfo CurrentTarget => channels.FirstOrDefault(c => c.Slot == "head")?.Target ?? LocalTargetInfo.Invalid;
    private float ModulePoints(string kind)
    {
        float[] rented = null;
        Span<float> points = fitted.Count <= 16 ? stackalloc float[16]
            : (rented = ArrayPool<float>.Shared.Rent(fitted.Count));
        try
        {
            int count = 0;
            foreach (var mount in fitted)
                if (mount.Part.def.GetModExtension<PartExtension>()?.moduleKind == kind)
                    points[count++] = ParagonUtility.Power(mount.Part);
            if (count == 0) return 0f;
            points = points.Slice(0, count);
            points.Sort();
            // Retain descending tail order and Sum<float>'s double accumulator.
            double remaining = 0d;
            for (int i = count - 2; i >= 0; i--) remaining += points[i];
            return points[count - 1] + 0.35f * (float)remaining;
        }
        finally
        {
            if (rented != null) ArrayPool<float>.Shared.Return(rented);
        }
    }
    public float DamageMultiplier => (1f + 0.05f * DamageLevel) * ExtraModifiers().DamageFactor;
    public float MainDamageMultiplier => DamageMultiplier * (1f + 0.2f * Math.Clamp(HeadPart?.TryGetComp<CompTurretPart>()?.Tier ?? 0, 0, 5));
    public float? MainShotDamage
    {
        get
        {
            var channel = channels.FirstOrDefault(c => c.Slot == "head" && c.Part == HeadPart);
            var verb = channel?.Verb;
            var projectile = (verb is Verb_ModularMortar ? CoreMortarProjectile : verb?.verbProps?.defaultProjectile)?.projectile;
            if (HeadPart == null || channel?.Gun == null || projectile == null) return null;
            bool instant = verb is Verb_Instant or Verb_Tesla;
            // Instant hits retain the gun; our projectile Launch clears equipment
            // before reading base damage. Match each path without firing a shot.
            Thing equipment = instant ? verb.EquipmentSource ?? channel.Gun : null;
            // Nominal main-shot damage excludes the temporary activeShotFactor.
            float damage = projectile.GetDamageAmount(equipment) * MainDamageMultiplier * MortarPayloadDamageFactor(channel.Gun);
            return instant ? damage : Math.Max(verb is Verb_ModularMortar ? 0 : 1, Mathf.RoundToInt(damage));
        }
    }
    internal static float ArmorDamageMultiplier(int level)
        => Math.Max(0.05f, 1f / (1f + 0.05f * Math.Max(0, level)));
    public float IncomingMultiplier => ArmorDamageMultiplier(ArmorLevel);
    private const float RangeBonusPerPoint = 0.025f;
    public float RangeMultiplier => (1f + RangeBonusPerPoint * ModulePoints("Range")) * ExtraModifiers().RangeFactor;
    public float AimingMultiplier => ExtraModifiers().AimingFactor / (1f + 0.1f * ModulePoints("Aiming"));
    public float CoolingMultiplier => ExtraModifiers().CoolingFactor / (1f + 0.1f * ModulePoints("Cooling"));
    public float TrackingDegreesPerTick => 3f * ExtraModifiers().TurnSpeedFactor;
    public float AccuracyMissFactor => ExtraModifiers().AccuracyMissFactor;
    public static bool HeadHasGuaranteedHits(PartExtension head)
        => head?.tesla == true || head?.gunDef?.GetModExtension<HeadAccuracyExtension>()?.guaranteedFlame == true;
    public static float HeadAccuracyMissFactor(PartExtension head, int tier)
    {
        if (head == null || HeadHasGuaranteedHits(head)) return 1f;
        float reduction = head.gunDef?.GetModExtension<HeadAccuracyExtension>()?.missReductionPerTier ?? 0.1f;
        if (!float.IsFinite(reduction)) reduction = 0.1f;
        return Mathf.Clamp01(1f - Math.Clamp(reduction, 0f, 0.2f) * Math.Clamp(tier, 0, 5));
    }
    internal bool IsCurrentHeadAccuracyWeapon(Thing equipment)
    {
        Thing head = HeadPart;
        if (equipment == null || channels == null || head == null || HeadHasGuaranteedHits(head.def.GetModExtension<PartExtension>())) return false;
        foreach (var channel in channels)
            if (channel.Slot == "head" && channel.Gun == equipment && channel.Part == head) return true;
        return false;
    }
    public bool PreventFriendlyFire => ExtraModifiers().PreventFriendlyFire;
    public float ShieldMaximum => Math.Max(0f, InstalledShieldCapacity + ExtraModifiers().ShieldBonus);
    public float ProjectileDamageMultiplier(Thing equipment)
    {
        var c = channels.FirstOrDefault(c => c.Gun == equipment);
        int tier = Math.Clamp(c?.Part?.TryGetComp<CompTurretPart>()?.Tier ?? 0, 0, 5);
        float partMultiplier = c?.Slot == "head" ? 1f + 0.2f * tier : 1f + 0.18f * tier;
        if (c?.Slot != "head" && ParagonUtility.IsParagon(c?.Part) && ParagonMapRegistry.IsEffective(c.Part))
            partMultiplier *= ParagonUtility.Factor(ParagonUtility.Settings(c.Part.def).auxiliaryDamageMultiplier, 4f);
        return activeShotFactor * DamageMultiplier * partMultiplier * MortarPayloadDamageFactor(equipment);
    }
    public float ProjectileArmorBonus => 0.01f * DamageLevel + ExtraModifiers().ArmorBonus;
    private static readonly MethodInfo clone = typeof(object).GetMethod("MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo burstCountCache = typeof(Verb).GetField("cachedBurstShotCount", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo burstIntervalCache = typeof(Verb).GetField("cachedTicksBetweenBurstShots", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo forcedMissRadius = typeof(VerbProperties).GetField("forcedMissRadius", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo classicForcedMissRadius = typeof(VerbProperties).GetField("forcedMissRadiusClassicMortars", BindingFlags.Instance | BindingFlags.NonPublic);
    public void RefreshConfiguration() => RebuildChannels();

    private void RebuildChannels()
    {
        targetCoordination?.Reconfigure();
        SynchronizeDurability();
        InvalidateEffects();
        RefreshPowerSource();
        RefreshMortarModules();
        UpdatePower();
        // Keep saved guns/parts intact when an active combat adapter failed.
        // Its changed Def templates cannot configure the old Core verbs.
        if (!CombatCompatibility.IsAvailable) return;
        if (GetComp<CompTurretWeaponAccuracy>() is { } accuracy) accuracy.MissFactor = AccuracyMissFactor;
        var old = channels ?? new List<FireChannel>();
        var updated = new List<FireChannel>();
        foreach (var mount in fitted)
        {
            var ext = mount.Part.def.GetModExtension<PartExtension>();
            if (ext?.gunDef == null || mount.Slot != "head" && ext.moduleKind != "Auxiliary") continue;
            var c = old.FirstOrDefault(c => c.Slot == mount.Slot && c.Part == mount.Part && c.Gun?.def == ext.gunDef)
                ?? new FireChannel { Slot = mount.Slot, Part = mount.Part, Gun = ThingMaker.MakeThing(ext.gunDef), Cooldown = 60 };
            CombatCompatibility.Provider?.PrepareChannel(this, c);
            float headAccuracy = mount.Slot == "head" ? HeadAccuracyMissFactor(ext, mount.Part.TryGetComp<CompTurretPart>()?.Tier ?? 0) : 1f;
            // Head upgrades affect only their gun; the shared caster and auxiliary
            // channels retain the existing fire-control module adjustment.
            if (c.Gun.TryGetComp<CompTurretWeaponAccuracy>() is { } gunAccuracy) gunAccuracy.MissFactor = AccuracyMissFactor * headAccuracy;
            foreach (var verb in c.Gun.TryGetComp<CompEquippable>().AllVerbs)
            {
                // Copy the template: changing a shared VerbProperties would alter other turrets.
                var template = ext.gunDef.Verbs.First(v => v.verbClass == verb.GetType());
                verb.verbProps = (VerbProperties)clone.Invoke(template, null);
                WeaponSounds.Apply(ext.gunDef, template, verb.verbProps);
                var rangeTemplate = mount.Slot == "head" && ext.usesShells ? MortarTargeting.VanillaProperties() ?? template : template;
                verb.verbProps.minRange = rangeTemplate.minRange;
                float nominalRange = rangeTemplate.range;
                verb.verbProps.range = Math.Max(1f, nominalRange * RangeMultiplier + 0.1f * RangeLevel + ExtraModifiers().RangeFlat);
                verb.verbProps.warmupTime = template.warmupTime * AimingMultiplier * (mount.Slot == "head" ? MortarSupportWarmupFactor : 1f);
                var specialist = mount.Slot == "head" ? ActiveSpecialist : null;
                if (specialist?.kind == "Cycle")
                {
                    verb.verbProps.warmupTime = Math.Max(1f / 60f, verb.verbProps.warmupTime
                        * SpecialistModuleExtension.Factor(specialist.warmupFactor, 1f));
                    verb.verbProps.burstShotCount = Math.Max(1, Mathf.RoundToInt(template.burstShotCount
                        * SpecialistModuleExtension.Factor(specialist.burstCountFactor, 1f)));
                }
                float scatterFactor = headAccuracy;
                if (mount.Slot == "head" && ext.usesShells)
                    scatterFactor *= ExtraModifiers().MortarScatterFactor;
                if (mount.Slot == "head" && ActiveParagon("FireControl") is { } controller)
                    scatterFactor *= ParagonFraction(ParagonUtility.Settings(controller.def).forcedMissRadiusFactor, 0.5f);
                if (scatterFactor < 1f && template.ForcedMissRadius > 0f)
                {
                    // Core's scatter precedes ShotReport and these fields have no
                    // public setters. Change only this instance's cloned properties.
                    if (forcedMissRadius?.FieldType == typeof(float) && classicForcedMissRadius?.FieldType == typeof(float))
                    {
                        float radius = (float)forcedMissRadius.GetValue(template);
                        float classicRadius = (float)classicForcedMissRadius.GetValue(template);
                        if (radius > 0f) forcedMissRadius.SetValue(verb.verbProps, radius * scatterFactor);
                        if (classicRadius > 0f) classicForcedMissRadius.SetValue(verb.verbProps, classicRadius * scatterFactor);
                    }
                    else Log.WarningOnce(L.T("MT_HeadScatterUnavailable"), GenText.StableStringHash("MT_HeadScatterUnavailable"));
                }
                if (ext.moduleKind == "Auxiliary")
                {
                    int tier = Math.Clamp(mount.Part.TryGetComp<CompTurretPart>()?.Tier ?? 0, 0, 5);
                    verb.verbProps.ticksBetweenBurstShots = Math.Max(2, template.ticksBetweenBurstShots - tier);
                    verb.verbProps.burstShotCount = template.burstShotCount + tier;
                    if (ParagonUtility.IsParagon(mount.Part) && ParagonMapRegistry.IsEffective(mount.Part))
                        verb.verbProps.burstShotCount = Math.Max(1, Mathf.RoundToInt(verb.verbProps.burstShotCount
                            * ParagonUtility.Factor(ParagonUtility.Settings(mount.Part.def).auxiliaryBurstMultiplier, 2f)));
                }
                if (mount.Slot == "head" && ActiveParagon("Cooling") is { } cooler)
                    verb.verbProps.ticksBetweenBurstShots = Math.Max(1, Mathf.CeilToInt(verb.verbProps.ticksBetweenBurstShots
                        * ParagonFraction(ParagonUtility.Settings(cooler.def).burstIntervalFactor, 0.5f)));
                // Core caches these property values without a public invalidation API.
                // Keep the current target/burst progress; refresh only the derived caches.
                if (burstCountCache?.FieldType == typeof(int?) && burstIntervalCache?.FieldType == typeof(int?))
                {
                    burstCountCache.SetValue(verb, null);
                    burstIntervalCache.SetValue(verb, null);
                }
                else Log.WarningOnce(L.T("MT_BurstCacheUnavailable"), GenText.StableStringHash("MT_BurstCacheUnavailable"));
                verb.caster = this;
                var channel = c;
                channel.CastComplete = () =>
                {
                    channel.Cooldown = channel.Slot == "head" ? MainCooldownTicks(ext, ActiveSpecialist)
                        : Math.Max(1, Mathf.RoundToInt(ext.cooldown * CoolingMultiplier * 60f));
                    channel.Warmup = ext.tesla ? 0 : Math.Max(1, Mathf.RoundToInt(channel.Verb.WarmupTime * 60f));
                };
                verb.castCompleteCallback = channel.CastComplete;
            }
            CombatCompatibility.Provider?.ConfigureChannel(this, c);
            // Old saves may retain a pre-change Tesla warmup; keep its cooldown
            // and active burst intact while removing the obsolete aiming delay.
            if (ext.tesla) c.Warmup = 0;
            // Older saves may retain ground fire after the manual order was cleared.
            // Automatic acquisition only returns Things; a cell needs a live order.
            if (c.Slot == "head" && !forcedTarget.IsValid && c.Target.IsValid && !c.Target.HasThing
                && !OffMapMortar.HasMission(this))
            { c.Verb.Reset(); c.Target = LocalTargetInfo.Invalid; c.Warmup = 0; c.QuickRetarget = false; }
            updated.Add(c);
        }
        foreach (var unused in old.Where(c => !updated.Contains(c))) unused.Gun?.Destroy();
        channels = updated;
        RefreshMortarGun();
        shieldEnergy = Math.Min(shieldEnergy, ShieldMaximum);
    }
    private void UpdatePower()
    {
        var power = GetComp<CompPowerTrader>();
        if (IsWrecked || selfPowered)
        {
            if (power != null)
            {
                power.PowerOutput = 0f;
                if (selfPowered && Spawned) power.PowerOn = !IsWrecked && InternalSupplyAvailable;
            }
            return;
        }
        if (power != null) power.PowerOutput = -(200f + fitted.Sum(p => ParagonUtility.PowerConsumption(p.Part)) + 10f * DamageLevel + 5f * ArmorLevel);
    }
    protected override void Tick()
    {
        base.Tick();
        if (IsWrecked) return;
        TickFlameTileVisuals();
        if (this.IsHashIntervalTick(60))
        {
            UpdatePower();
            if (CombatCompatibility.Provider == null && AutoReload && Head?.usesShells == true && Shells <= 6) QueueReload();
            TickMortarAutoReload();
        }
        if (shieldDelay > 0) shieldDelay--;
        bool powered = Powered;
        if (powered && shieldDelay == 0)
        {
            float maximum = ShieldMaximum;
            shieldEnergy = Math.Min(maximum, shieldEnergy + maximum / 7200f);
        }
        bool enabled = powered && !IsStunned && !HoldFire && CombatCompatibility.CheckReady();
        foreach (var c in channels)
        {
            c.CompatibilityBlocked = false;
            CombatCompatibility.Provider?.TickChannel(this, c);
            if (enabled && c.CompatibilityBlocked) continue;
            if (!enabled || CombatCompatibility.Provider == null && c.Slot == "head" && Head?.usesShells == true && Shells <= 0
                || c.Verb is Verb_ModularMortar && MortarTargeting.SourceRoofed(this))
            {
                targetCoordination?.Release(c);
                c.Verb.Reset(); c.Target = LocalTargetInfo.Invalid; c.Warmup = 0; c.QuickRetarget = false; continue;
            }
            if (targetCoordination != null && c.Target.HasThing && !UncoordinatedTarget(c, c.Target)
                && !targetCoordination.OwnsCurrent(c))
            {
                targetCoordination.Release(c);
                c.Verb.Reset(); c.Target = LocalTargetInfo.Invalid; c.Warmup = 0; c.QuickRetarget = false;
            }
            if (c.Slot == "head" && c.Verb is Verb_ModularBurst burst && burst.Bursting)
                UpdateBurstHandoff(c, burst);
            c.Verb.VerbTick();
            if (c.Verb.Bursting) continue;
            if (c.Cooldown > 0) { c.Cooldown--; continue; }
            if (!ValidTarget(c, c.Target))
            {
                targetCoordination?.Release(c);
                if (c.Target.IsValid) c.QuickRetarget = c.Slot == "head" && ActiveParagon("Servo") != null
                    && c.Target.Thing is Pawn oldPawn && (oldPawn.Dead || oldPawn.Downed);
                c.Target = LocalTargetInfo.Invalid; c.Warmup = 0;
            }
            if (!c.Target.IsValid)
            {
                if (!this.IsHashIntervalTick(TargetScanInterval)) continue;
                c.Target = FindTarget(c);
                if (!c.Target.IsValid) continue;
                c.Warmup = c.Part.def.GetModExtension<PartExtension>().tesla ? 0 : Math.Max(1, Mathf.RoundToInt(c.Verb.WarmupTime * 60f));
                if (c.QuickRetarget && c.Warmup > 0 && ActiveParagon("Servo") is { } servo)
                    c.Warmup = Math.Max(1, Mathf.CeilToInt(c.Warmup
                        * ParagonFraction(ParagonUtility.Settings(servo.def).retargetWarmupFactor, 0.5f)));
                c.QuickRetarget = false;
            }
            if (c.Warmup > 0) { c.Warmup--; if (c.Warmup > 0) continue; }
            if (c.Slot == "head" && !Head.tesla && !IsFacing(c.Target)) continue;
            // Core Reset clears this callback (hold fire, power loss or forced target).
            // Restore it for every new burst so completion still starts cooldown/warmup.
            c.Verb.castCompleteCallback = c.CastComplete;
            c.OpeningShotReady = true;
            if (c.Verb.TryStartCastOn(c.Target)) OnAttackedTarget(c.Target);
            else { targetCoordination?.Release(c); c.Target = LocalTargetInfo.Invalid; c.Cooldown = 15; }
        }
        // Keep the last facing without a target; module rotors animate separately.
        if (CurrentTarget.IsValid) aimAngle = Mathf.MoveTowardsAngle(aimAngle, (CurrentTarget.CenterVector3 - DrawPos).AngleFlat(), TrackingDegreesPerTick);
    }
    private bool ValidTarget(FireChannel c, LocalTargetInfo t)
    {
        if (!t.IsValid || t.HasThing && (!t.Thing.Spawned || t.Thing.Destroyed || !SameFloor(this, t.Thing))) return false;
        if (t.Thing is Pawn pawn && (pawn.Downed || pawn.Dead || pawn.IsPrisoner)) return false;
        if (t.HasThing && t != forcedTarget && !t.Thing.HostileTo(this)) return false;
        return c.Verb.CanHitTarget(t);
    }
    private LocalTargetInfo FindTarget(FireChannel c)
    {
        if (c.Slot == "head" && OffMapMortar.HasMission(this)) return OffMapMortar.LocalAim(this);
        if (c.Slot == "head" && forcedTarget.IsValid && ValidTarget(c, forcedTarget)) return forcedTarget;
        var coordination = targetCoordination;
        var query = coordination == null ? default : coordination.BeginQuery(c);
        if (c.Slot != "head" && ParagonUtility.IsParagon(c.Part) && ParagonMapRegistry.IsEffective(c.Part)
            && c.Part.def.GetModExtension<PartExtension>()?.moduleKind == "Auxiliary")
        {
            float radius = Math.Clamp(ParagonUtility.Settings(c.Part.def).defenseRadius, 0f, 8f);
            // Restrict priority to actual active combat threats and normal hit rules.
            Pawn closeThreat = null;
            int closestDistance = int.MaxValue;
            foreach (Pawn pawn in Map.mapPawns.AllPawnsSpawned)
            {
                if (!(pawn.Position.DistanceToSquared(Position) <= radius * radius) || !pawn.HostileTo(this)
                    || pawn.ThreatDisabled(this) || !ValidTarget(c, pawn)
                    || targetCoordination != null && !targetCoordination.CanReserve(c, pawn)) continue;
                int distance = pawn.Position.DistanceToSquared(Position);
                if (closeThreat != null && distance >= closestDistance) continue;
                closeThreat = pawn;
                closestDistance = distance;
            }
            if (closeThreat != null)
            {
                if (coordination != targetCoordination || coordination != null && !coordination.TryAcquire(c, closeThreat, query)) return LocalTargetInfo.Invalid;
                return closeThreat;
            }
        }
        var flags = TargetScanFlags.NeedThreat | TargetScanFlags.NeedAutoTargetable;
        if (c.Verb.ProjectileFliesOverhead()) flags |= TargetScanFlags.NeedNotUnderThickRoof;
        else if (CombatCompatibility.Provider == null && c.Verb.verbProps.requireLineOfSight) flags |= TargetScanFlags.NeedLOSToAll | TargetScanFlags.LOSBlockableByGas;
        ChannelSearcher searcher = GetTargetSearcher(c);
        Thing selected = (Thing)AttackTargetFinder.BestShootTargetFromCurrentPosition(searcher, flags, searcher.Validator);
        if (selected != null && (coordination != targetCoordination || coordination != null && !coordination.TryAcquire(c, selected, query))) return LocalTargetInfo.Invalid;
        return selected;
    }
    public override void DeSpawn(DestroyMode mode = DestroyMode.Vanish)
    {
        if (targetCoordination != null) SetTargetCoordination(null);
        // Cells, target reservations and unfinished bursts belong to the old map.
        // Keep physical guns, ammunition, orders and elapsed cooldowns intact.
        OffMapMortar.Cancel(this);
        forcedTarget = LocalTargetInfo.Invalid;
        foreach (var channel in channels)
        {
            channel.Verb?.Reset();
            channel.Target = LocalTargetInfo.Invalid;
            channel.Warmup = 0; channel.QuickRetarget = false; channel.OpeningShotReady = false;
            channel.CompatibilityBlocked = false;
        }
        base.DeSpawn(mode);
    }
    public override void OrderAttack(LocalTargetInfo targ)
    {
        if (targ.IsValid && !CombatCompatibility.CheckReady())
        { Messages.Message(L.T("MT_CombatAdapterUnavailable"), this, MessageTypeDefOf.RejectInput); return; }
        if (IsWrecked) { Messages.Message(L.T("MT_WreckCannotAttack"), this, MessageTypeDefOf.RejectInput); return; }
        if (targ.IsValid && (AttackVerb == null || targ.HasThing && !SameFloor(this, targ.Thing) || !AttackVerb.CanHitTarget(targ)))
        { Messages.Message(L.T("MT_CannotAttack"), this, MessageTypeDefOf.RejectInput); return; }
        targetCoordination?.ReleaseHead();
        OffMapMortar.Cancel(this);
        forcedTarget = targ.IsValid ? targ : LocalTargetInfo.Invalid;
        // Cancelling also discards the cached cell and any unfinished burst.
        var c = channels.FirstOrDefault(x => x.Slot == "head");
        if (c != null) { c.Verb.Reset(); c.Target = LocalTargetInfo.Invalid; c.Warmup = 0; c.QuickRetarget = false; }
    }
    public override void PreApplyDamage(ref DamageInfo dinfo, out bool absorbed)
    {
        base.PreApplyDamage(ref dinfo, out absorbed);
        if (dinfo.Amount > 0f && dinfo.Def != DamageDefOf.Deterioration)
            NoteShieldCombatActivity(Find.TickManager.TicksGame);
        if (absorbed || dinfo.Amount <= 0f) return;
        if (dinfo.Def == DamageDefOf.EMP)
        {
            shieldEnergy = 0f; shieldDelay = 1200; return;
        }
        // Environmental deterioration is not shielded or reduced by combat upgrades.
        if (dinfo.Def == DamageDefOf.Deterioration) return;
        float amount = dinfo.Amount;
        if (Powered && shieldEnergy > 0f && !dinfo.Def.ignoreShields)
        {
            float taken = Math.Min(shieldEnergy, amount); shieldEnergy -= taken; amount -= taken;
            shieldDelay = shieldEnergy <= 0f ? 1200 : 300;
            if (taken > 0f && shieldEnergy <= 0f) TryEmergencyShield();
        }
        dinfo.SetAmount(amount * IncomingMultiplier);
        absorbed = amount <= 0f;
    }
    protected override void DrawAt(Vector3 drawLoc, bool flip = false)
    {
        base.DrawAt(drawLoc, flip); TurretRenderer.Draw(this, drawLoc);
        if (!IsWrecked) { DrawBeams(); DrawTeslaPulseVisuals(); }
    }
    private LongRangeOverlay longRangeOverlay;
    public override void DrawExtraSelectionOverlays()
    {
        base.DrawExtraSelectionOverlays();
        if (Spawned && AttackVerb != null)
        {
            float range = AttackVerb.EffectiveRange;
            if (float.IsFinite(range) && range > 0f)
            {
                // Core's cell-based ring has a fixed precomputed radius limit.
                // Keep the actual weapon range when drawing long-range upgrades.
                if (range <= GenRadial.MaxRadialPatternRadius) GenDraw.DrawRadiusRing(Position, range);
                else (longRangeOverlay ??= new LongRangeOverlay()).Draw(Map, Position, range);
            }
        }
        if (Spawned && Head?.usesShells == true && AttackVerb?.verbProps is { minRange: > 0f } mortar
            && mortar.minRange <= GenRadial.MaxRadialPatternRadius)
            GenDraw.DrawRadiusRing(Position, mortar.minRange, Color.red);
    }
}
