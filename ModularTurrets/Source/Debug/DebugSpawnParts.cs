using System;
using System.Collections.Generic;
using System.Linq;
using LudeonTK;
using ModularTurrets.Batteries;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace ModularTurrets;

internal static class DebugSpawnParts
{
    private static bool CanUse => Prefs.DevMode && Current.ProgramState == ProgramState.Playing
        && Find.CurrentMap != null && !WorldRendererUtility.WorldSelected;

    [DebugAction("ModularTurrets", "Spawn graded part", allowedGameStates = AllowedGameStates.PlayingOnMap)]
    private static DebugActionNode SpawnGradedPart() => new(L.T("MT_DebugSpawnPart"))
    {
        labelGetter = () => L.T("MT_DebugSpawnPart"),
        visibilityGetter = () => CanUse,
        childGetter = Categories
    };

    private static List<DebugActionNode> Categories()
    {
        if (!CanUse) return new();
        return new()
        {
            Group("MT_DebugSpawnHeads", def => IsTurretPart(def) && def.GetModExtension<PartExtension>().isHead),
            Group("MT_DebugSpawnModules", def => IsTurretPart(def) && !def.GetModExtension<PartExtension>().isHead),
            Group("MT_DebugSpawnBatteryModules", IsBatteryModule)
        };
    }

    private static DebugActionNode Group(string key, Func<ThingDef, bool> matches) => new(L.T(key))
    {
        labelGetter = () => L.T(key),
        childGetter = () => DefDatabase<ThingDef>.AllDefsListForReading.Where(matches).OrderBy(L.Label)
            .Select(def => new DebugActionNode(L.Label(def))
            {
                labelGetter = () => L.Label(def),
                childGetter = () => Grades(def)
            }).ToList()
    };

    private static List<DebugActionNode> Grades(ThingDef def)
    {
        var nodes = new List<DebugActionNode>();
        int maximum = MaximumGrade(def);
        for (int grade = 0; grade <= maximum; grade++)
        {
            int selected = grade;
            nodes.Add(Tool(def, selected, false));
        }
        if (IsTurretPart(def) && maximum == 5 && ParagonUtility.IsEligible(def)) nodes.Add(Tool(def, 5, true));
        return nodes;
    }

    private static DebugActionNode Tool(ThingDef def, int grade, bool paragon)
    {
        string Label()
        {
            string gradeLabel = paragon ? L.T("MT_DebugSpawnParagon") : IsBatteryModule(def)
                ? L.T("MT_DebugSpawnLevel", grade)
                : L.T("MT_DebugSpawnTier", new[] { "0", "I", "II", "III", "IV", "V" }[grade]);
            return L.T("MT_DebugSpawnChoice", L.Label(def), gradeLabel);
        }
        return new DebugActionNode(Label(), DebugActionType.ToolMap, () => SpawnAtMouse(def, grade, paragon))
        {
            labelGetter = Label,
            visibilityGetter = () => CanUse
        };
    }

    private static bool IsTurretPart(ThingDef def) => def?.category == ThingCategory.Item
        && def.GetModExtension<PartExtension>() != null
        && def.comps?.Any(comp => typeof(CompTurretPart).IsAssignableFrom(comp.compClass)) == true;

    private static bool IsBatteryModule(ThingDef def) => def?.category == ThingCategory.Item
        && def.GetModExtension<BatteryModuleExtension>() != null
        && def.comps?.Any(comp => typeof(CompBatteryModuleUpgrade).IsAssignableFrom(comp.compClass)) == true;

    internal static int MaximumGrade(ThingDef def)
    {
        if (IsTurretPart(def)) return Building_ModularTurret.CanUpgradePart(def) ? 5 : 0;
        if (IsBatteryModule(def)) return def.GetModExtension<BatteryModuleExtension>().MaximumLevel;
        throw new ArgumentException("Definition is not a supported modular part.", nameof(def));
    }

    // Return an ordinary detached physical item; normal comps retain grade, effects and valuation.
    internal static Thing CreatePart(ThingDef def, int grade, bool paragon)
    {
        int maximum = MaximumGrade(def);
        if (grade < 0 || grade > maximum) throw new ArgumentOutOfRangeException(nameof(grade));
        if (paragon && (!IsTurretPart(def) || grade != 5 || !ParagonUtility.IsEligible(def)))
            throw new ArgumentException("Only eligible tier-V modules can be Paragon.", nameof(paragon));
        Thing item = ThingMaker.MakeThing(def, def.MadeFromStuff ? GenStuff.DefaultStuffFor(def) : null);
        try
        {
            if (IsTurretPart(def))
            {
                var part = item.TryGetComp<CompTurretPart>() ?? throw new InvalidOperationException("Missing turret part comp.");
                part.Tier = grade;
                part.IsParagon = paragon;
                // Reuse the existing estimated investment fallback for newly graded debug items.
                _ = part.UpgradeInvestmentEntries;
            }
            else
            {
                var module = item.TryGetComp<CompBatteryModuleUpgrade>() ?? throw new InvalidOperationException("Missing battery module comp.");
                for (int next = 1; next <= grade; next++)
                    if (!module.TryAdvanceTo(next)) throw new InvalidOperationException("Battery module rejected a valid grade.");
            }
            return item;
        }
        catch
        {
            if (!item.Spawned && !item.Destroyed) item.Destroy(DestroyMode.Vanish);
            throw;
        }
    }

    private static void SpawnAtMouse(ThingDef def, int grade, bool paragon)
    {
        if (!CanUse) return;
        Map map = Find.CurrentMap;
        IntVec3 cell = UI.MouseCell();
        if (!cell.InBounds(map))
        {
            Messages.Message(L.T("MT_DebugSpawnOutsideMap"), MessageTypeDefOf.RejectInput, false);
            return;
        }
        if (!FloorCompatibility.TryGetViewFloor(map, out int floor)
            || !FloorCompatibility.TryEnter(map, floor, out var scope))
        {
            Messages.Message(L.T("MT_DebugSpawnFloorUnavailable"), MessageTypeDefOf.RejectInput, false);
            return;
        }
        Thing item = null;
        using (scope)
        {
            try
            {
                item = CreatePart(def, grade, paragon);
                if (!FloorCompatibility.TrySetFloor(item, floor))
                    throw new InvalidOperationException(L.T("MT_DebugSpawnFloorUnavailable"));
                if (!GenPlace.TryPlaceThing(item, cell, map, ThingPlaceMode.Near, out _,
                    null, candidate => candidate.InBounds(map)))
                {
                    if (!item.Spawned && !item.Destroyed) item.Destroy(DestroyMode.Vanish);
                    Messages.Message(L.T("MT_DebugSpawnNoSpace"), MessageTypeDefOf.RejectInput, false);
                }
            }
            catch (Exception error)
            {
                if (item != null && !item.Spawned && !item.Destroyed) item.Destroy(DestroyMode.Vanish);
                Messages.Message(L.T("MT_DebugSpawnFailed", error.Message), MessageTypeDefOf.RejectInput, false);
            }
        }
    }
}
