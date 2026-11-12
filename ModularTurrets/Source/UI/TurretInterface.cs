using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using UnityEngine;
using Verse;
using static ModularTurrets.AssemblyConsole;

namespace ModularTurrets;

public sealed partial class Building_ModularTurret
{
    private static int MaterialDisplayOrder(ThingDef resource) => resource?.defName switch
    {
        "Steel" => 0,
        "Plasteel" => 1,
        "Uranium" => 2,
        "ComponentIndustrial" => 3,
        "ComponentSpacer" => 4,
        _ => 5
    };
    public override IEnumerable<Gizmo> GetGizmos()
    {
        foreach (Gizmo gizmo in GridGizmos(base.GetGizmos()))
            if (gizmo is not Designator_Install) yield return gizmo;
        if (Faction != Faction.OfPlayer) yield break;
        yield return ReinstallGizmo();
        if (IsWrecked) yield return WreckRepairCommand();
        if (ShieldMaximum > 0f && Find.Selector.SingleSelectedThing == this)
            yield return new Gizmo_ModularTurretShieldStatus(this);
        yield return new Command_Action
        {
            defaultLabel = L.T("MT_ManageLabel"),
            defaultDesc = L.T("MT_ManageDesc"),
            icon = ContentFinder<Texture2D>.Get("ModularTurrets/UI/Manage"),
            action = () => Find.WindowStack.Add(new AssemblyWindow(this))
        };
        if (!IsWrecked && AttackVerb != null)
        {
            yield return new Command_Target
            {
                defaultLabel = L.T("MT_AttackLabel"),
                defaultDesc = L.T("MT_AttackDesc"),
                icon = ContentFinder<Texture2D>.Get("UI/Commands/Attack"),
                targetingParams = new TargetingParameters { canTargetPawns = true, canTargetBuildings = true, canTargetLocations = true },
                action = OrderAttack
            };
            yield return new Command_Toggle
            {
                defaultLabel = L.T("MT_HoldFireLabel"),
                defaultDesc = L.T("MT_HoldFireDesc"),
                icon = ContentFinder<Texture2D>.Get("UI/Commands/HoldFire"),
                isActive = () => HoldFire,
                toggleAction = () => HoldFire = !HoldFire
            };
        }
        if (forcedTarget.IsValid)
            yield return new Command_Action
            {
                defaultLabel = L.T("MT_ClearTargetLabel"),
                defaultDesc = L.T("MT_ClearTargetDesc"),
                icon = ContentFinder<Texture2D>.Get("UI/Commands/Halt"),
                action = () => OrderAttack(LocalTargetInfo.Invalid)
            };
        if (Head?.usesShells == true)
        {
            yield return new Command_Toggle
            {
                defaultLabel = L.T("MT_AutoReloadLabel"),
                defaultDesc = L.T("MT_MortarAutoReloadDesc"),
                icon = DefDatabase<ThingDef>.GetNamed("Shell_HighExplosive").uiIcon,
                isActive = () => AutoReload,
                toggleAction = () => AutoReload = !AutoReload
            };
            yield return new Command_Action
            {
                defaultLabel = L.T("MT_MortarAmmoSelectValue", MortarAmmoName(SelectedMortarAmmo)),
                defaultDesc = L.T("MT_MortarAmmoSelectDesc"),
                icon = (SelectedMortarAmmo ?? DefDatabase<ThingDef>.GetNamed("Shell_HighExplosive")).uiIcon,
                action = OpenMortarAmmoMenu
            };
        }
        if (Head?.usesShells == true || CanUnloadMortarAmmo)
        {
            var unload = new Command_Action
            {
                defaultLabel = L.T("MT_MortarUnloadLabel"), defaultDesc = L.T("MT_MortarUnloadDesc"),
                icon = (LoadedMortarAmmo ?? SelectedMortarAmmo ?? DefDatabase<ThingDef>.GetNamed("Shell_HighExplosive")).uiIcon,
                action = UnloadMortarAmmoFromUI
            };
            if (!CanUnloadMortarAmmo) unload.Disable(L.T("MT_MortarAmmoActionFailed"));
            yield return unload;
        }
        // Optional UI follows the core controls, so an adapter's iterator
        // cannot prevent attack, hold-fire or assembly commands appearing.
        foreach (var gizmo in OffMapMortar.GetGizmos(this)) yield return gizmo;
    }

    public override string GetInspectString()
    {
        var text = new StringBuilder(base.GetInspectString().TrimEnd());
        void Line(string value) { if (text.Length > 0) text.AppendLine(); text.Append(value); }
        if (IsWrecked) Line(L.T("MT_WreckInspect"));
        if (!CombatCompatibility.IsAvailable) Line(L.T("MT_CombatAdapterUnavailable"));
        Line(HeadPart == null ? L.T("MT_InspectNoHead") : L.T("MT_InspectHead", L.Label(HeadPart.def)));
        Line(L.T("MT_InspectBase", DamageLevel, ArmorLevel, RangeLevel));
        if (AttackVerb != null) Line(L.T("MT_InspectCombat", MainDamageMultiplier, AttackVerb.EffectiveRange));
        if (Head?.usesShells == true) Line(L.T("MT_MortarAmmoStock", MortarAmmoName(LoadedMortarAmmo ?? SelectedMortarAmmo), LoadedMortarRounds));
        if (HoldFire) Line(L.T("MT_InspectHoldFire"));
        if (PendingOrder != null) Line(L.T("MT_InspectQueue", L.T(AssemblyBlocked ? "MT_AssemblyReturnBlocked" : "MT_QueueName"), Orders.Count, OrderLabel(PendingOrder)));
        return text.ToString();
    }

    private static string SlotLabel(string slot) => slot switch
    {
        "head" => L.T("MT_SlotHead"), "left" => L.T("MT_SlotLeft"), "right" => L.T("MT_SlotRight"), "top" => L.T("MT_SlotTop"), "rear" => L.T("MT_SlotRear"), _ => slot ?? string.Empty
    };

    private static string MortarAmmoName(ThingDef ammunition) => ammunition == null ? "—" : L.Label(ammunition);

    private void OpenMortarAmmoMenu()
    {
        var options = new List<FloatMenuOption>();
        foreach (var ammunition in MortarAmmoTypes)
        {
            ThingDef selected = ammunition;
            options.Add(new FloatMenuOption(MortarAmmoName(selected), () =>
            {
                if (!TrySelectMortarAmmo(selected))
                    Messages.Message(L.T("MT_MortarAmmoActionFailed"), this, MessageTypeDefOf.RejectInput);
            }));
        }
        if (options.Count > 0) Find.WindowStack.Add(new FloatMenu(options));
    }

    private void UnloadMortarAmmoFromUI()
    {
        if (!TryUnloadMortarAmmo())
            Messages.Message(L.T("MT_MortarAmmoActionFailed"), this, MessageTypeDefOf.RejectInput);
    }

    private static string OperationLabel(TurretOperation kind) => kind switch
    {
        TurretOperation.Damage => L.T("MT_OperationDamage"), TurretOperation.Armor => L.T("MT_OperationArmor"), TurretOperation.Range => L.T("MT_OperationRange"), _ => L.T("MT_OperationUnknown")
    };

    private static string ResearchLabel(string defName) => L.Label(DefDatabase<ResearchProjectDef>.GetNamedSilentFail(defName));

    private static string OrderLabel(TurretOrder order) => order.Kind switch
    {
        TurretOperation.Restore => L.T("MT_WreckRestoreOrder"),
        TurretOperation.Install => L.T("MT_OrderInstall", SlotLabel(order.Slot), L.Label(order.PartDef)),
        TurretOperation.Remove => L.T("MT_OrderRemove", SlotLabel(order.Slot)),
        TurretOperation.UpgradeModule => order.TargetParagon ? L.T("MT_ParagonOrder", SlotLabel(order.Slot)) : L.T("MT_OrderPartUpgrade", SlotLabel(order.Slot), order.TargetTier),
        TurretOperation.Reload => L.T("MT_OrderReload"),
        _ => L.T("MT_OrderBaseUpgrade", OperationLabel(order.Kind), order.TargetTier)
    };
}
