using System;
using System.Collections.Generic;
using global::CombatExtended;
using global::CombatExtended.WorldObjects;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace ModularTurrets.Compat.CombatExtended;

// Adapt only MT's order/channel orchestration. CE still owns its mortar
// trajectory, range units, markers, ammo, TravelingShell and world damage.
public sealed class RemoteMortarBackend : IRemoteMortarBackend
{
    private static Verb_MountedMortarCE Mortar(Building_ModularTurret owner) => owner?.AttackVerb as Verb_MountedMortarCE;
    private static WorldObject TargetObject(GlobalTargetInfo target) => target.WorldObject ?? target.Map?.Parent;
    private static float WorldRange(Verb_MountedMortarCE verb) => verb?.projectilePropsCE?.shellingProps?.range ?? -1f;
    private static int RingRadius(float nativeRange, PlanetLayer layer)
    {
        float factor = layer.Def.rangeDistanceFactor;
        if (!float.IsFinite(factor) || factor <= 0f || !float.IsFinite(nativeRange) || nativeRange <= 0f) return 0;
        // CE truncates shelling range first, then scales tiles for the viewed layer.
        return (int)Math.Min(OffMapMortarRange.Maximum(layer), Math.Floor(Math.Floor(nativeRange) / factor));
    }
    private static float DisplayRange(float nativeRange, PlanetLayer layer)
        => Math.Min(nativeRange, OffMapMortarRange.Maximum(layer) * layer.Def.rangeDistanceFactor);
    private static bool RequiresMarker(Building_ModularTurret owner)
        => Controller.settings.RequireArtilleryMarkerForOffMapArtillery
            && (owner.def.GetModExtension<OrbitalTurretExtension>()?.isMarkMandatory ?? true);

    public bool HasMission(Building_ModularTurret owner) => Mortar(owner)?.globalTargetInfo.IsValid == true;

    public bool Order(Building_ModularTurret owner, Map destination, IntVec3 cell, out string reason)
    {
        if (destination == null || destination.Parent == null || !Find.Maps.Contains(destination) || !cell.InBounds(destination))
        { reason = "CE_ArtilleryTarget_InvalidTarget".Translate(); return false; }
        var target = new GlobalTargetInfo(cell, destination);
        return OrderWorld(owner, target, out reason);
    }

    private bool OrderWorld(Building_ModularTurret owner, GlobalTargetInfo target, out string reason)
    {
        var verb = Mortar(owner);
        if (!Validate(owner, verb, target, checkMarker: true, out reason)) return false;
        if (!owner.Powered) { reason = "CannotFire".Translate(); return false; }
        if (!SourceOpen(owner)) { reason = "CannotFire".Translate() + ": " + "Roofed".Translate(); return false; }
        // This calls the normal local cancellation once. Our Cancel does not
        // call OrderAttack, so the backend dispatch cannot recurse.
        owner.OrderAttack(LocalTargetInfo.Invalid);
        verb.PrepareWorldTarget(owner, target);
        if (!verb.mokeTargetInfo.IsValid || !verb.mokeTargetInfo.Cell.InBounds(owner.Map))
        { Cancel(owner); reason = "CE_OutofBounds".Translate(); return false; }
        // Do not call TryStartShelling here: that would fire immediately and
        // bypass the saved MT channel cooldown/warmup. Native Tick starts CE.
        return true;
    }

    internal static bool Validate(Building_ModularTurret owner, Verb_MountedMortarCE verb, GlobalTargetInfo target, bool checkMarker, out string reason)
    {
        reason = null;
        if (owner?.Spawned != true || owner.IsWrecked || owner.Faction != Faction.OfPlayer || verb == null || !ReferenceEquals(owner.AttackVerb, verb))
        { reason = L.T("MT_OffMapNeedsMortar"); return false; }
        WorldObject worldObject = TargetObject(target);
        if (!target.IsValid || !target.Tile.Valid || target.Tile == owner.Map.Tile
            || worldObject == null || worldObject.Destroyed || !worldObject.Spawned
            || worldObject is DestroyedSettlement || worldObject.def == WorldObjectDefOf.DestroyedSettlement)
        { reason = "CE_ArtilleryTarget_InvalidTarget".Translate(); return false; }
        if (target.Tile.Layer != owner.Map.Tile.Layer && owner.def.GetModExtension<OrbitalTurretExtension>() == null)
        { reason = "CE_Message_ArtilleryBadLayer".Translate(); return false; }
        float range = WorldRange(verb);
        if (!float.IsFinite(range) || range <= 0f || !verb.WithinShuttleLimit(owner.Map.Tile, target.Tile, range)
            || ShellingUtility.GetDistancePlanetTiles(owner.Map.Tile, target.Tile, (int)Math.Min(int.MaxValue, range * 1.5d)) > range)
        { reason = "CE_ArtilleryTarget_DestinationBeyondMaximumRange".Translate(); return false; }
        if (target.IsMapTarget)
        {
            Map map = target.Map;
            if (map == null || map == owner.Map || !Find.Maps.Contains(map) || !target.Cell.InBounds(map))
            { reason = "CE_ArtilleryTarget_InvalidTarget".Translate(); return false; }
            if (!FloorCompatibility.TryEnter(map, 0, out var scope))
            { reason = L.T("MT_OffMapFloorUnavailable"); return false; }
            using (scope)
            {
                // CE requires this roof/spotter test when its marker setting
                // is enabled. A lost marker later uses CE's normal miss penalty.
                if (checkMarker && Controller.settings.RequireArtilleryMarkerForOffMapArtillery)
                {
                    RoofDef roof = map.roofGrid.RoofAt(target.Cell);
                    if (roof != null && roof != RoofDefOf.RoofConstructed)
                    { reason = "CE_ArtilleryTarget_NoThickRoof".Translate(); return false; }
                    if (RequiresMarker(owner) && target.Cell.GetFirstThing<ArtilleryMarker>(map) == null)
                    { reason = "CE_ArtilleryTarget_MustTargetMark".Translate(); return false; }
                }
            }
        }
        else if (worldObject.GetComponent<HealthComp>() == null && (worldObject as MapParent)?.Map == null)
        { reason = "CE_ArtilleryTarget_InvalidTarget".Translate(); return false; }
        return true;
    }

    internal static bool SourceOpen(Building_ModularTurret owner)
    {
        if (owner?.Spawned != true || !FloorCompatibility.TryGetFloor(owner, out int floor)
            || !FloorCompatibility.TryEnter(owner.Map, floor, out var scope)) return false;
        using (scope) return Mortar(owner)?.Projectile?.projectile?.flyOverhead != true || !owner.Position.Roofed(owner.Map);
    }

    public LocalTargetInfo LocalAim(Building_ModularTurret owner)
    {
        var verb = Mortar(owner);
        if (verb == null || !verb.globalTargetInfo.IsValid) return LocalTargetInfo.Invalid;
        if (verb.WorldSourceMapId != owner.Map?.uniqueID
            || !Validate(owner, verb, verb.globalTargetInfo, checkMarker: false, out _))
        { Cancel(owner); return LocalTargetInfo.Invalid; }
        return SourceOpen(owner) ? verb.mokeTargetInfo : LocalTargetInfo.Invalid;
    }

    public void Cancel(Building_ModularTurret owner)
        => ClearWorldMission(owner);

    internal static void ClearWorldMission(Building_ModularTurret owner)
    {
        if (owner == null) return;
        foreach (var channel in owner.CombatChannels)
        {
            if (channel.Slot != "head" || channel.Verb is not Verb_MountedMortarCE verb) continue;
            verb.ClearWorldTarget();
            verb.Reset(); channel.Target = LocalTargetInfo.Invalid; channel.Warmup = 0; channel.QuickRetarget = false;
        }
    }

    public IEnumerable<Gizmo> GetGizmos(Building_ModularTurret owner)
    {
        if (owner == null || owner.Faction != Faction.OfPlayer || owner.IsWrecked) yield break;
        foreach (var channel in owner.CombatChannels)
        {
            if (channel.Gun?.TryGetComp<CompAmmoUser>() is { } ammo)
            {
                if (CombatAdapter.EnsureAmmoSelection(ammo, out string reason))
                    foreach (var gizmo in CollectGizmos(ammo.CompGetGizmosExtra, channel.Gun, "ammo")) yield return gizmo;
                else yield return UnavailableGizmo(reason);
            }
            if (channel.Gun?.TryGetComp<CompFireModes>() is { } modes)
                foreach (var gizmo in CollectGizmos(modes.GenerateGizmos, channel.Gun, "fire modes")) yield return gizmo;
        }
        var verb = Mortar(owner);
        if (owner.Spawned && owner.Powered && WorldRange(verb) > 0f)
            yield return new Command_Action
            {
                defaultLabel = "CE_ArtilleryTargetLabel".Translate(), defaultDesc = "CE_ArtilleryTargetDesc".Translate(),
                icon = ContentFinder<Texture2D>.Get("UI/Buttons/AttackWorldTile"), hotKey = KeyBindingDefOf.Misc5,
                action = () => ChooseWorld(owner)
            };
        if (HasMission(owner))
            yield return new Command_Action
            {
                defaultLabel = "CommandStopForceAttack".Translate(), defaultDesc = "CommandStopForceAttackDesc".Translate(),
                icon = ContentFinder<Texture2D>.Get("UI/Commands/Halt"), action = () => Cancel(owner)
            };
    }

    private static IEnumerable<Gizmo> CollectGizmos(Func<IEnumerable<Gizmo>> factory, Thing gun, string feature)
    {
        try { return new List<Gizmo>(factory()); }
        catch (Exception error)
        {
            string reason = L.T("MT_CEGizmoUnavailable", L.Label(gun.def),
                L.T(feature == "ammo" ? "MT_CEFeatureAmmo" : "MT_CEFeatureFireModes"), error.Message);
            Log.ErrorOnce("[Modular Turrets CE] " + reason, GenText.StableStringHash("MTCEGizmos:" + gun.def.defName + ":" + feature));
            return new[] { UnavailableGizmo(reason) };
        }
    }

    private static Gizmo UnavailableGizmo(string reason)
    {
        var command = new Command_Action { defaultLabel = L.T("MT_CESettingsError"), defaultDesc = reason, icon = BaseContent.BadTex };
        command.Disable(reason);
        return command;
    }

    private void ChooseWorld(Building_ModularTurret owner)
    {
        var verb = Mortar(owner); if (verb == null) return;
        CameraJumper.TryJump(CameraJumper.GetWorldTarget(owner)); Find.WorldSelector.ClearSelection();
        var origin = owner.Tile;
        PlanetLayer projectedLayer = null;
        PlanetTile projectedCenter = origin;
        bool orbital = owner.def.GetModExtension<OrbitalTurretExtension>() != null;
        void Draw()
        {
            var layer = PlanetLayer.Selected;
            if (!owner.Spawned || layer == null || layer != origin.Layer && !orbital) return;
            if (layer != projectedLayer)
            {
                projectedCenter = layer == origin.Layer ? origin : layer.GetClosestTile_NewTemp(origin);
                projectedLayer = layer;
            }
            OffMapMortarTargeting.DrawRangeRing(projectedCenter, RingRadius(WorldRange(verb), layer));
        }
        Find.WorldTargeter.BeginTargeting(target => SelectWorld(owner, target), canTargetTiles: true, closeWorldTabWhenFinished: true,
            onUpdate: Draw,
            extraLabelGetter: target => target.Tile.Valid
                ? "CE_ArtilleryTarget_Distance".Translate().Formatted(ShellingUtility.GetDistancePlanetTiles(owner.Tile, target.Tile),
                    DisplayRange(WorldRange(verb), target.Tile.Layer)) : "",
            canSelectTarget: target => target.HasWorldObject && target.Tile != owner.Tile
                && ((target.WorldObject as MapParent)?.Map != null || target.WorldObject.GetComponent<HealthComp>() != null));
    }

    private bool SelectWorld(Building_ModularTurret owner, GlobalTargetInfo target)
    {
        if (!Validate(owner, Mortar(owner), target, checkMarker: false, out string reason))
        { Messages.Message(reason, MessageTypeDefOf.RejectInput, false); return false; }
        Map map = (target.WorldObject as MapParent)?.Map;
        bool mandatory = owner.def.GetModExtension<OrbitalTurretExtension>()?.isMarkMandatory ?? true;
        if (map != null && (!mandatory || map.mapPawns.AnyPawnBlockingMapRemoval))
        {
            Find.WorldTargeter.StopTargeting();
            CameraJumper.TryJump(new IntVec3(map.Size.x / 2, 0, map.Size.z / 2), map, CameraJumper.MovementMode.Pan);
            if (!FloorCompatibility.TryShowFloor(map, 0))
            { Messages.Message(L.T("MT_OffMapFloorUnavailable"), MessageTypeDefOf.RejectInput, false); return false; }
            Find.Targeter.BeginTargeting(new TargetingParameters { canTargetLocations = true, canTargetBuildings = true, canTargetHumans = true },
                local => { if (!Order(owner, map, local.Cell, out string why)) Messages.Message(why, MessageTypeDefOf.RejectInput, false); },
                highlightAction: local => GenDraw.DrawTargetHighlight(local),
                targetValidator: local => Validate(owner, Mortar(owner), new GlobalTargetInfo(local.Cell, map), true, out _));
            return false;
        }
        Faction targetFaction = target.WorldObject.Faction;
        if (targetFaction == Faction.OfPlayer) return false;
        if (targetFaction != null)
        {
            if (targetFaction.RelationWith(owner.Faction, true) == null) targetFaction.TryMakeInitialRelationsWith(owner.Faction);
            if (!targetFaction.HostileTo(owner.Faction) && !targetFaction.Hidden)
            {
                Find.WindowStack.Add(new Dialog_MessageBox("CE_ArtilleryTarget_AttackingAllies".Translate().Formatted(target.WorldObject.Label, targetFaction.Name),
                    "CE_Yes".Translate(), () => { if (!OrderWorld(owner, target, out string why)) Messages.Message(why, MessageTypeDefOf.RejectInput, false); Find.WorldTargeter.StopTargeting(); },
                    "CE_No".Translate(), () => Find.WorldTargeter.StopTargeting(), buttonADestructive: true));
                return false;
            }
        }
        if (OrderWorld(owner, target, out reason)) return true;
        Messages.Message(reason, MessageTypeDefOf.RejectInput, false); return false;
    }
}
