using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace ModularTurrets;

// Convert only our pre-UFT bills at the next recipe-start boundary. A loaded
// JobDriver already doing recipe work keeps its existing workLeft and job.
[StaticConstructorOnStartup]
internal static class UnfinishedCraftingCompatibility
{
    private const string OwnerId = "local.modularturrets.unfinished-crafting";
    private static bool initialized;
    private static readonly HashSet<string> Recipes = new(StringComparer.Ordinal)
    {
        "MT_MakeHead_Autocannon", "MT_MakeHead_Sniper", "MT_MakeHead_DualAutocannon",
        "MT_MakeHead_DualSniper", "MT_MakeHead_DualMortar", "MT_MakeHead_Flamethrower",
        "MT_MakeHead_Minigun", "MT_MakeHead_Laser", "MT_MakeHead_Missile",
        "MT_MakeHead_Tesla", "MT_MakeHead_Quantum", "MT_MakeModule_Range",
        "MT_MakeModule_Shield", "MT_MakeModule_Auxiliary", "MT_MakeModule_Aiming",
        "MT_MakeModule_Cooling", "MT_MakeModule_Fan", "MT_MakeModule_FireControl",
        "MT_MakeModule_SpecialistAutocannon", "MT_MakeModule_SpecialistSniper",
        "MT_MakeModule_SpecialistMinigun", "MT_MakeModule_SpecialistQuantum",
        "MT_MakeModule_SpecialistFlamethrower", "MT_MakeModule_SpecialistLaser",
        "MT_MakeModule_SpecialistTesla"
    };
    private static readonly FieldInfo[] BillFields = new[] { typeof(Bill), typeof(Bill_Production) }
        .SelectMany(t => t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
        .Where(f => !f.IsStatic && !f.IsInitOnly && !f.IsLiteral).ToArray();

    static UnfinishedCraftingCompatibility() => Initialize();

    internal static void Initialize()
    {
        if (initialized) return;
        try { ApplyPatch(); initialized = true; }
        catch (Exception error) { Log.ErrorOnce(L.T("MT_UnfinishedPatchFailed", error.Message), 1724213701); }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ApplyPatch()
    {
        var original = AccessTools.DeclaredMethod(typeof(Toils_Recipe), nameof(Toils_Recipe.MakeUnfinishedThingIfNeeded), Type.EmptyTypes);
        if (original == null || original.ReturnType != typeof(Toil))
            throw new MissingMethodException(typeof(Toils_Recipe).FullName, "MakeUnfinishedThingIfNeeded()");
        new Harmony(OwnerId).Patch(original, postfix: new HarmonyMethod(typeof(UnfinishedCraftingCompatibility), nameof(WrapStart))
        { priority = Priority.Last });
    }

    private static bool IsLegacy(Bill bill) => bill?.GetType() == typeof(Bill_Production)
        && Recipes.Contains(bill.recipe?.defName ?? string.Empty)
        && bill.recipe.unfinishedThingDef?.defName == "UnfinishedComponent";

    private static void WrapStart(Toil __result)
    {
        if (__result?.initAction == null)
        {
            Log.ErrorOnce(L.T("MT_UnfinishedPatchFailed", "Core unfinished-crafting toil has no initAction."), 1724213701);
            return;
        }
        Action original = __result.initAction;
        __result.initAction = () =>
        {
            var job = __result.actor?.CurJob;
            if (IsLegacy(job?.bill))
            {
                Bill oldBill = job.bill;
                try { job.bill = ConvertLegacyBill((Bill_Production)oldBill); }
                catch (Exception error)
                {
                    Log.ErrorOnce(L.T("MT_UnfinishedMigrationFailed", oldBill.recipe.label, error.Message), 1724213702);
                    __result.actor.jobs.EndCurrentJob(JobCondition.Incompletable);
                    return;
                }
            }
            original();
        };
    }

    internal static Bill_ProductionWithUft ConvertLegacyBill(Bill_Production oldBill)
    {
        if (!IsLegacy(oldBill)) throw new ArgumentException("Bill is not an eligible legacy crafting bill.");
        BillStack stack = oldBill.billStack ?? throw new InvalidOperationException("Legacy bill has no stack.");
        string loadId = oldBill.GetUniqueLoadID();
        int index = stack.Bills.IndexOf(oldBill);
        if (index < 0)
        {
            // A queued job may still reference the old object after another job
            // converted its stack entry. Reconnect it to that same saved bill.
            var existing = stack.Bills.OfType<Bill_ProductionWithUft>().FirstOrDefault(b =>
                b.recipe == oldBill.recipe && b.GetUniqueLoadID() == loadId);
            return existing ?? throw new InvalidOperationException("Legacy bill is no longer in its stack.");
        }

        // Core Clone preserves GetType(), so it cannot perform this migration.
        // Copy the two Core base classes, including private ID/restriction/store
        // fields. This replaces an object, so filters and groups remain shared
        // with any still-open settings window instead of being reset or cloned.
        var converted = new Bill_ProductionWithUft();
        foreach (var field in BillFields) field.SetValue(converted, field.GetValue(oldBill));
        foreach (var field in BillFields)
            if (!Equals(field.GetValue(converted), field.GetValue(oldBill)))
                throw new InvalidOperationException("Bill field did not survive conversion: " + field.Name);
        if (converted.GetUniqueLoadID() != loadId || converted.billStack != stack || converted.BoundUft != null)
            throw new InvalidOperationException("Converted bill identity or ownership is invalid.");

        // Commit only after all fields and identity are verified. Do not call
        // Delete/AddBill or InitializeAfterClone: they invalidate references or
        // issue a new ID. Core's original initAction creates/binds the UFT next.
        stack.Bills[index] = converted;
        return converted;
    }
}
