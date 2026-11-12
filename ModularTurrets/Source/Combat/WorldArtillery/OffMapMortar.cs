using System;
using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace ModularTurrets;

// One purpose adapter: Core owns world transit and payload serialization.
// A normal local cast still owns scatter, module effects, burst timing and ammo.
public static class OffMapMortar
{
    public const string FlightDefName = "MT_OffMapMortarFlight";
    [ThreadStatic] private static ShotScope current;

    internal sealed class ShotScope : IDisposable
    {
        internal readonly Verb_ModularMortar Verb;
        internal readonly IntVec3 Aim;
        internal readonly OffMapMortarMission Mission;
        private readonly ShotScope previous;
        internal ShotScope(Verb_ModularMortar verb, IntVec3 aim)
        { Verb = verb; Aim = aim; Mission = verb.OffMapMission; previous = current; current = this; }
        public void Dispose() { current = previous; }
    }

    public static bool HasMission(Building_ModularTurret turret)
        => CombatCompatibility.Remote is { } backend ? backend.HasMission(turret)
            : turret?.AttackVerb is Verb_ModularMortar mortar && mortar.OffMapMission != null;

    public static bool Order(Building_ModularTurret turret, Map destination, IntVec3 cell, out string reason)
    {
        if (!CombatCompatibility.CheckReady()) { reason = L.T("MT_CombatAdapterUnavailable"); return false; }
        if (CombatCompatibility.Remote is { } backend) return backend.Order(turret, destination, cell, out reason);
        reason = null;
        if (turret?.Spawned != true || turret.IsWrecked || turret.Faction != Faction.OfPlayer
            || turret.AttackVerb is not Verb_ModularMortar mortar)
        { reason = L.T("MT_OffMapNeedsMortar"); return false; }
        if (!ValidDestination(turret, destination, cell))
        { reason = L.T("MT_OffMapNeedsLoadedMap"); return false; }
        if (!OffMapMortarRange.Within(turret.Map.Tile, destination.Tile))
        { reason = L.T("MT_OffMapOutOfRange", OffMapMortarRange.Maximum(destination.Tile.Layer)); return false; }
        if (DefDatabase<WorldObjectDef>.GetNamedSilentFail(FlightDefName) == null)
        { reason = L.T("MT_OffMapUnavailable"); return false; }
        if (!OffMapGroundScope.TryEnter(destination, out var ground))
        { reason = L.T("MT_OffMapFloorUnavailable"); return false; }
        ground?.Dispose();
        if (!FindLocalAim(mortar).IsValid)
        { reason = L.T("MT_OffMapCannotLaunch"); return false; }
        turret.OrderAttack(LocalTargetInfo.Invalid);
        mortar.OffMapMission = new OffMapMortarMission { Destination = destination.Parent, Cell = cell, SourceMapId = turret.Map.uniqueID };
        return true;
    }

    public static void Cancel(Building_ModularTurret turret)
    {
        if (CombatCompatibility.Remote is { } backend) { backend.Cancel(turret); return; }
        if (turret?.AttackVerb is not Verb_ModularMortar mortar || mortar.OffMapMission == null) return;
        mortar.OffMapMission = null;
        turret.ResetOffMapAim();
    }

    // Core transport census only. Optional backends own a different flight type.
    public static int ActiveFlights()
    {
        int count = 0;
        foreach (var flight in Find.WorldObjects.AllWorldObjects)
            if (flight.def.defName == FlightDefName) count++;
        return count;
    }

    internal static bool ValidDestination(Building_ModularTurret turret, Map map, IntVec3 cell)
        => map != null && map != turret.Map && map.Parent != null && map.Parent.Spawned
            && ReferenceEquals(map.Parent.Map, map)
            && map.Tile.Valid && turret.Map.Tile.Valid && map.Tile != turret.Map.Tile
            && cell.InBounds(map);

    private static bool MissionValid(Verb_ModularMortar verb)
    {
        var mission = verb.OffMapMission;
        return verb.caster is Building_ModularTurret turret && turret.Spawned && !turret.IsWrecked
            && turret.Faction == Faction.OfPlayer && turret.Map.uniqueID == mission?.SourceMapId
            && ReferenceEquals(turret.AttackVerb, verb) && ValidDestination(turret, mission.Destination?.Map, mission.Cell)
            && OffMapMortarRange.Within(turret.Map.Tile, mission.Destination.Tile, mission);
    }

    private static LocalTargetInfo FindLocalAim(Verb_ModularMortar verb)
    {
        if (verb.caster is not Building_ModularTurret turret || !turret.Spawned) return LocalTargetInfo.Invalid;
        if (!FloorCompatibility.TryGetFloor(turret, out int floor)
            || !FloorCompatibility.TryEnter(turret.Map, floor, out var scope)) return LocalTargetInfo.Invalid;
        using (scope)
        {
            int minimum = Math.Max(3, Mathf.CeilToInt(verb.verbProps.minRange + 1f));
            int distance = Mathf.FloorToInt(Math.Min(verb.EffectiveRange - 1f, Math.Max(20, minimum)));
            if (distance < minimum) return LocalTargetInfo.Invalid;
            foreach (var direction in GenAdj.CardinalDirections)
            {
                IntVec3 cell = turret.Position + direction * distance;
                if (cell.InBounds(turret.Map) && verb.CanHitTarget(cell)) return cell;
            }
            return LocalTargetInfo.Invalid;
        }
    }

    public static LocalTargetInfo LocalAim(Building_ModularTurret turret)
        => CombatCompatibility.Remote is { } backend ? backend.LocalAim(turret)
            : turret?.AttackVerb is Verb_ModularMortar mortar ? LocalAim(mortar) : LocalTargetInfo.Invalid;

    public static LocalTargetInfo LocalAim(Verb_ModularMortar verb)
    {
        if (CombatCompatibility.Remote is { } backend) return backend.LocalAim(verb.caster as Building_ModularTurret);
        if (MissionValid(verb)) return FindLocalAim(verb);
        if (verb.caster is Building_ModularTurret turret) Cancel(turret);
        return LocalTargetInfo.Invalid;
    }

    internal static ShotScope BeginShot(Verb_ModularMortar verb, out bool allowed)
    {
        // A backend prepares its actual verb. An unmigrated Core mortar must
        // not accidentally emit ordinary shells under a different combat API.
        if (!CombatCompatibility.IsAvailable || CombatCompatibility.Remote != null) { allowed = false; return null; }
        allowed = true;
        if (verb.OffMapMission == null) return null;
        allowed = MissionValid(verb);
        if (!allowed && verb.caster is Building_ModularTurret turret) Cancel(turret);
        return allowed ? new ShotScope(verb, verb.CurrentTarget.Cell) : null;
    }

    internal static void Transfer(Projectile_ModularExplosive projectile, Thing launcher, Thing equipment)
    {
        ShotScope shot = current;
        if (shot == null || !ReferenceEquals(shot.Verb.caster, launcher) || !ReferenceEquals(shot.Verb.EquipmentSource, equipment)) return;
        var turret = (Building_ModularTurret)launcher;
        IntVec3 destination = shot.Mission.Cell + (projectile.usedTarget.Cell - shot.Aim);
        // Scatter may leave the map just as a normal mortar shell may miss an
        // edge cell. Never clamp a miss into a guaranteed hit on a border target.
        var flight = (TravellingTransporters)WorldObjectMaker.MakeWorldObject(DefDatabase<WorldObjectDef>.GetNamed(FlightDefName));
        flight.Tile = turret.Map.Tile;
        flight.SetFaction(turret.Faction);
        flight.destinationTile = shot.Mission.Destination.Tile;
        flight.arrivalAction = new TransportersArrivalAction_ModularMortar(shot.Mission.Destination, destination, shot.Mission.Cell);
        var payload = new ActiveTransporterInfo { leaveSlag = false };
        // World transit owns progress. A despawned projectile needs a Map to
        // tick; keep its saved state untouched until normal arrival resumes it.
        payload.innerContainer.dontTickContents = true;
        projectile.DeSpawn();
        if (!payload.innerContainer.TryAdd(projectile, false))
        {
            projectile.Destroy();
            throw new InvalidOperationException("Modular mortar projectile could not enter its Core flight container.");
        }
        flight.AddTransporter(payload, justLeftTheMap: false);
        Find.WorldObjects.Add(flight);
    }

    public static IEnumerable<Gizmo> GetGizmos(Building_ModularTurret turret)
    {
        if (!CombatCompatibility.IsAvailable) yield break;
        if (CombatCompatibility.Remote is { } backend)
        {
            foreach (var gizmo in backend.GetGizmos(turret)) yield return gizmo;
            yield break;
        }
        if (turret.Faction != Faction.OfPlayer || turret.IsWrecked || turret.AttackVerb is not Verb_ModularMortar) yield break;
        yield return new Command_Action
        {
            defaultLabel = L.T("MT_OffMapOrderLabel"), defaultDesc = L.T("MT_OffMapOrderDesc", OffMapMortarRange.SurfaceMaximum), icon = TexCommand.Attack,
            action = () => OffMapMortarTargeting.Begin(turret)
        };
        if (HasMission(turret)) yield return new Command_Action
        {
            defaultLabel = L.T("MT_OffMapCancelLabel"), defaultDesc = L.T("MT_OffMapCancelDesc"), icon = ContentFinder<Texture2D>.Get("UI/Commands/Halt"),
            action = () => Cancel(turret)
        };
    }

}
