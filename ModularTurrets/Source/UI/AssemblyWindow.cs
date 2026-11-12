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

    // This partial owns layout and queue controls. PartWindow and PartDetailsWindow
    // keep their selection/upgrade state separate without changing nested type names.
    private sealed partial class AssemblyWindow : Window
    {
        private readonly Building_ModularTurret turret;
        private Vector2 slotScroll, baseScroll, queueScroll;
        private string selectedSlot = "head";
        private bool baseTab;
        private PartWindow partWindow;
        protected override float Margin => 0f;
        public override Vector2 InitialSize => WindowSize;

        public AssemblyWindow(Building_ModularTurret turret)
        {
            this.turret = turret;
            doCloseX = false;
            doWindowBackground = false;
            draggable = true;
            absorbInputAroundWindow = true;
        }

        public override void PostClose() { partWindow?.Close(); base.PostClose(); }

        public override void DoWindowContents(Rect inRect)
        {
            if (!turret.Spawned || turret.Destroyed) { Close(); return; }
            var oldFont = Text.Font; var oldAnchor = Text.Anchor; var oldColor = GUI.color;
            bool oldEnabled = GUI.enabled;
            try
            {
                Text.Font = GameFont.Small; Text.Anchor = TextAnchor.UpperLeft; GUI.color = Color.white;
                bool powered = turret.Powered;
                if (DrawHeader(inRect, L.T("MT_WindowTitle"), powered ? 5 : 1, powered ? AccentColor : AmberColor,
                    L.T(powered ? "MT_PowerOn" : "MT_PowerOff"))) { Close(); return; }
                inRect = inRect.ContractedBy(20f);
                inRect.yMin += 42f;
                var slots = new List<string> { "head" };
                if (turret.Head?.mounts != null) slots.AddRange(turret.Head.mounts.Select(m => m.id).Where(id => !string.IsNullOrEmpty(id) && id != "head").Distinct());
                if (!slots.Contains(selectedSlot)) selectedSlot = "head";
                float width = Math.Max(1f, inRect.width);
                string summary = L.T("MT_UIR_CombatDamage", turret.MainShotDamage?.ToString("0.##") ?? "—", turret.IncomingMultiplier,
                    turret.AttackVerb?.EffectiveRange ?? 0f, -(turret.GetComp<CompPowerTrader>()?.PowerOutput ?? 0f));
                float aim = turret.AttackVerb == null || turret.AttackVerb is Verb_Tesla ? 0f : Math.Max(1, Mathf.RoundToInt(turret.AttackVerb.WarmupTime * 60f)) / 60f;
                float cooldown = turret.MainCooldownSeconds;
                string support = L.T("MT_UIR_SupportSeconds", aim, cooldown, turret.shieldEnergy,
                    turret.ShieldMaximum, L.T(turret.Powered ? "MT_PowerOn" : "MT_PowerOff"));
                float y = inRect.y;
                TooltipHandler.TipRegion(new Rect(inRect.x, y, width, LabelHeight(summary, width)), MainDamageTip());
                DrawParagraph(inRect.x, ref y, width, summary); DrawParagraph(inRect.x, ref y, width, support);
                Rect partsTab = new(inRect.x, y, (width - 8f) / 2f, 32f);
                Rect upgradesTab = new(partsTab.xMax + 8f, partsTab.y, partsTab.width, partsTab.height);
                string baseResearchReason = ResearchReason("MT_InfiniteEngineering");
                if (baseResearchReason != null) baseTab = false;
                if (ControlButton(partsTab, L.T("MT_UIR_PartsTab"), active: !baseTab)) baseTab = false;
                if (ControlButton(upgradesTab, L.T("MT_UIR_BaseTab"), enabled: baseResearchReason == null, active: baseTab)) baseTab = true;
                if (baseResearchReason != null) TooltipHandler.TipRegion(upgradesTab, baseResearchReason);
                float queueHeight = Math.Min(200f, Math.Max(140f, inRect.height * 0.25f));
                Rect queue = new(inRect.x, inRect.yMax - queueHeight, width, queueHeight);
                float controlsHeight = turret.Head?.usesShells == true || turret.CanUnloadMortarAmmo ? (width < 600f ? 102f : 68f) : 0f;
                Rect body = new(inRect.x, partsTab.yMax + 10f, width, Math.Max(60f, queue.y - partsTab.yMax - 22f - controlsHeight));
                if (baseTab) DrawBaseUpgrades(body);
                else DrawSlotSelector(body, slots);
                if (controlsHeight > 0f) DrawAssemblyControls(new Rect(inRect.x, body.yMax + 4f, width, controlsHeight));
                DrawQueue(queue);
            }
            finally { Text.Font = oldFont; Text.Anchor = oldAnchor; GUI.color = oldColor; GUI.enabled = oldEnabled; }
        }

        private static void ControlToggle(Rect rect, string label, ref bool value)
        {
            DrawMetalPanel(rect, value ? new Color(0.22f, 0.24f, 0.23f) : CardColor);
            Rect switchRect = new(rect.xMax - 36f, rect.center.y - 7f, 26f, 14f);
            Widgets.DrawBoxSolid(switchRect, RecessColor);
            Widgets.DrawBoxSolid(new Rect(value ? switchRect.xMax - 11f : switchRect.x + 2f, switchRect.y + 2f, 9f, 10f), value ? AmberColor : EdgeColor);
            TextAnchor anchor = Text.Anchor; Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(new Rect(rect.x + 10f, rect.y, rect.width - 52f, rect.height), label); Text.Anchor = anchor;
            if (GUI.enabled && Widgets.ButtonInvisible(rect)) value = !value;
        }
        private bool HasSlot(string slot) => slot == "head" || turret.Head?.mounts?.Any(m => m.id == slot) == true;
        private TurretOrder PartUpgrade(string slot) => turret.Orders.FirstOrDefault(o => o.Slot == slot && o.Kind == TurretOperation.UpgradeModule);
        private string UpgradeStatus(TurretOrder order) => order.TargetParagon
            ? L.T(turret.PendingOrder == order && turret.CanWork ? "MT_ParagonWorking" : "MT_ParagonWaiting")
            : L.T(turret.PendingOrder == order && turret.CanWork ? "MT_UIR_UpgradeWorking" : "MT_UIR_UpgradeWaiting", order.TargetTier);
        private static string StageLabel(Thing part, int tier) => part != null && !CanUpgradePart(part.def)
            ? L.T("MT_SpecialistFixedTier") : ParagonUtility.IsParagon(part)
                ? L.T(ParagonMapRegistry.IsEffective(part) ? "MT_ParagonLabel" : "MT_ParagonRestrictedStage") : L.T("MT_TierLabel", tier);
        private static bool NextIsParagon(Thing part, int next) => next == 6 && !ParagonUtility.IsParagon(part) && ParagonUtility.IsEligible(part?.def);
        private static bool HasNextStage(Thing part, int next) => !ParagonUtility.IsParagon(part) && (next <= 5 || NextIsParagon(part, next));
        private static void DrawParagonBadge(Rect icon, Thing part)
        {
            if (!ParagonUtility.IsParagon(part)) return;
            Rect badge = new(icon.xMax - 22f, icon.yMax - 22f, 22f, 22f);
            Widgets.DrawBoxSolid(badge, RecessColor);
            var oldAnchor = Text.Anchor; var oldColor = GUI.color;
            Text.Anchor = TextAnchor.MiddleCenter; GUI.color = ParagonMapRegistry.IsEffective(part) ? AccentColor : AmberColor;
            Widgets.Label(badge, L.T("MT_ParagonBadge"));
            Text.Anchor = oldAnchor; GUI.color = oldColor;
        }
        private string PartStageLabel(string slot, int tier)
        {
            if (turret.PartAt(slot) is { } part && !CanUpgradePart(part.def))
                return L.T("MT_SpecialistFixedTier");
            TurretOrder active = PartUpgrade(slot);
            return active == null ? StageLabel(turret.PartAt(slot), tier)
                : L.T("MT_ParagonStageActive", StageLabel(turret.PartAt(slot), tier), UpgradeStatus(active));
        }
        private void OpenPartWindow(string slot, bool selecting)
        {
            if (!selecting && (!CanUpgradePart(turret.PartAt(slot)?.def)
                || UpgradeResearchReason(turret.PartAt(slot), (turret.PartAt(slot)?.TryGetComp<CompTurretPart>()?.Tier ?? 0) + 1) != null)) return;
            selectedSlot = slot;
            partWindow?.Close();
            partWindow = new PartWindow(this, slot, selecting);
            Find.WindowStack.Add(partWindow);
        }

        private void DrawSlotSelector(Rect rect, List<string> slots)
        {
            DrawMetalPanel(rect, PanelColor, true);
            Rect viewport = rect.ContractedBy(10f);
            float width = Math.Max(1f, viewport.width - 20f);
            bool compact = width < 720f;
            int columns = compact ? (width >= 470f ? 2 : 1) : 3;
            float cardWidth = Math.Min(340f, (width - (columns - 1) * 12f) / columns);
            float nameWidth = Math.Max(1f, cardWidth - 110f);
            float cardHeight = Math.Max(174f, 90f + slots.Max(s =>
            {
                Thing part = turret.PartAt(s);
                float name = LabelHeight(part == null ? L.T("MT_UIR_EmptySlot") : L.Label(part.def), nameWidth);
                float tier = part == null ? 0f : LabelHeight(PartStageLabel(s, part.TryGetComp<CompTurretPart>()?.Tier ?? 0), nameWidth) + 6f;
                return Math.Max(76f, name + tier);
            }));
            int centerRow = slots.Contains("top") ? 1 : 0, extra = 0;
            var positions = new List<(string slot, int col, int row)>();
            foreach (string slot in slots)
            {
                (int col, int row) = compact ? (positions.Count % columns, positions.Count / columns) : slot switch
                {
                    "head" => (1, centerRow), "left" => (0, centerRow), "right" => (2, centerRow),
                    "top" => (1, 0), "rear" => (1, centerRow + 1), _ => (extra % 3, centerRow + 2 + extra++ / 3)
                };
                positions.Add((slot, col, row));
            }
            int rows = positions.Max(p => p.row) + 1;
            float contentHeight = rows * cardHeight + (rows - 1) * 12f;
            float startX = Math.Max(0f, (width - (columns * cardWidth + (columns - 1) * 12f)) / 2f);
            float startY = Math.Max(0f, (viewport.height - contentHeight) / 2f);
            Widgets.BeginScrollView(viewport, ref slotScroll, new Rect(0f, 0f, width, Math.Max(viewport.height, contentHeight)));
            if (!compact)
            {
                Vector2 hub = new(startX + cardWidth + 12f + cardWidth / 2f, startY + centerRow * (cardHeight + 12f) + cardHeight / 2f);
                foreach (var p in positions.Where(p => p.slot != "head"))
                {
                    Vector2 point = new(startX + p.col * (cardWidth + 12f) + cardWidth / 2f, startY + p.row * (cardHeight + 12f) + cardHeight / 2f);
                    Vector2 bend = new(point.x, hub.y);
                    Color line = turret.PartAt(p.slot) == null ? EdgeColor : AccentColor;
                    if (bend != hub) Widgets.DrawLine(hub, bend, line, 2f);
                    if (point != bend) Widgets.DrawLine(bend, point, line, 2f);
                }
            }
            foreach (var p in positions)
                DrawSlotCard(new Rect(startX + p.col * (cardWidth + 12f), startY + p.row * (cardHeight + 12f), cardWidth, cardHeight), p.slot);
            Widgets.EndScrollView();
        }

        private void DrawSlotCard(Rect rect, string slot)
        {
            Thing part = turret.PartAt(slot);
            int tier = part?.TryGetComp<CompTurretPart>()?.Tier ?? 0;
            bool busy = turret.Orders.Any(o => o.Slot == slot);
            DrawMetalPanel(rect, selectedSlot == slot ? new Color(0.20f, 0.27f, 0.28f) : CardColor);
            Widgets.DrawBoxSolid(new Rect(rect.x + 2f, rect.y + 3f, 3f, rect.height - 6f), selectedSlot == slot ? AccentColor : EdgeColor);
            Widgets.Label(new Rect(rect.x + 12f, rect.y + 7f, rect.width - 100f, 24f), SlotLabel(slot));
            if (part != null && ControlButton(new Rect(rect.xMax - 62f, rect.y + 6f, 52f, 26f), L.T("MT_PartDetailsButton")))
                Find.WindowStack.Add(new PartDetailsWindow(this, slot, part));
            Rect icon = new(rect.x + 12f, rect.y + 38f, 76f, 76f);
            Widgets.DrawBoxSolid(icon, RecessColor);
            if (part != null)
            {
                SlotPartIcons.Draw(icon.ContractedBy(3f), part, slot);
                DrawParagonBadge(icon, part);
                TooltipHandler.TipRegion(icon, PartDescription(slot, part) + "\n\n" + L.T("MT_PartDetailsHint"));
                if (Widgets.ButtonInvisible(icon)) Find.WindowStack.Add(new PartDetailsWindow(this, slot, part));
            }
            else
            {
                Text.Anchor = TextAnchor.MiddleCenter; Text.Font = GameFont.Medium;
                Widgets.Label(icon, L.T("MT_EmptySlotMark")); Text.Font = GameFont.Small; Text.Anchor = TextAnchor.UpperLeft;
                bool canInstall = !busy && turret.Orders.Count < 100;
                TooltipHandler.TipRegion(icon, canInstall ? L.T("MT_UIR_InstallPart") : L.T(busy ? "MT_SlotHasOrder" : "MT_QueueFull"));
                if (canInstall && Widgets.ButtonInvisible(icon)) OpenPartWindow(slot, true);
            }
            float textY = rect.y + 38f;
            DrawParagraph(rect.x + 98f, ref textY, rect.width - 110f, part == null ? L.T("MT_UIR_EmptySlot") : L.Label(part.def));
            if (part != null) DrawParagraph(rect.x + 98f, ref textY, rect.width - 110f, PartStageLabel(slot, tier));
            if (busy)
            {
                Widgets.DrawBoxSolid(new Rect(rect.xMax - 78f, rect.y + 13f, 6f, 6f), AmberColor);
                TooltipHandler.TipRegion(new Rect(rect.xMax - 86f, rect.y + 7f, 22f, 22f), L.T("MT_UIR_SlotPending"));
            }
            Rect buttons = new(rect.x + 10f, rect.yMax - 44f, rect.width - 20f, 34f);
            if (part == null)
            {
                bool allowed = !busy && turret.Orders.Count < 100;
                if (ActionButton(buttons, L.T(busy ? "MT_InstallPending" : "MT_UIR_InstallPart"), allowed,
                    busy ? L.T("MT_SlotHasOrder") : L.T("MT_AssemblyRules"))) OpenPartWindow(slot, true);
            }
            else
            {
                float half = (buttons.width - 8f) / 2f;
                string researchReason = !CanUpgradePart(part.def) ? L.T("MT_SpecialistNoUpgrade")
                    : ParagonUtility.IsParagon(part) ? L.T("MT_ParagonMax") : UpgradeResearchReason(part, tier + 1) ?? ParagonConflictReason(part, tier + 1);
                if (ActionButton(new Rect(buttons.x, buttons.y, half, buttons.height), L.T(NextIsParagon(part, tier + 1) ? "MT_ParagonUpgrade" : "MT_UIR_UpgradePart"),
                    researchReason == null, researchReason)) OpenPartWindow(slot, false);
                string reason = slot == "head" && turret.fitted.Any(m => m.Slot != "head") ? L.T("MT_RemoveHeadFirst")
                    : busy ? L.T("MT_SlotHasOrder") : turret.Orders.Count >= 100 ? L.T("MT_QueueFull") : null;
                if (ActionButton(new Rect(buttons.x + half + 8f, buttons.y, half, buttons.height), L.T("MT_UIR_DetachPart"), reason == null, reason ?? L.T("MT_RemoveTip")))
                {
                    selectedSlot = slot;
                    if (!turret.QueueRemove(slot)) Reject(L.T("MT_SlotHasOrder"));
                }
            }
        }

        private string ShieldModuleStatus(Thing part)
        {
            if (part.def.GetModExtension<PartExtension>()?.moduleKind != "Shield") return string.Empty;
            var lines = new List<string>();
            if (turret.ShieldRechargeDelayTicks > 0)
                lines.Add(L.T("MT_ShieldStatusDelay", Math.Ceiling(turret.ShieldRechargeDelayTicks / 60f)));
            if (turret.ActiveParagon("Shield") == part && turret.ShieldEmergencyCooldownTicks is int emergency)
                lines.Add(emergency > 0 ? L.T("MT_ShieldStatusEmergencyDelay", Math.Ceiling(emergency / 60f))
                    : L.T(turret.Powered ? "MT_ShieldStatusEmergencyReady" : "MT_ShieldStatusEmergencyNoPower"));
            return lines.Count == 0 ? string.Empty : "\n\n" + string.Join("\n", lines);
        }

        private string PartDescription(string slot, Thing part, bool installed = true)
        {
            int tier = part.TryGetComp<CompTurretPart>()?.Tier ?? 0;
            bool specialist = part.def.GetModExtension<SpecialistModuleExtension>() != null;
            string text = L.Label(part.def) + "\n" + StageLabel(part, tier) + "\n\n" + PartEffectText(part, tier, context: turret);
            if (CanUpgradePart(part.def)) text += "\n\n" + TurretPartContext.Description(part.def, turret);
            if (specialist && !turret.CanMountPart(slot, part.def)) text += "\n\n" + L.T("MT_SpecialistInactive");
            else if (installed && specialist && turret.fitted.FirstOrDefault(m => m.Part?.def == part.def && turret.CanMountPart(m.Slot, m.Part.def))?.Part != part)
                text += "\n\n" + L.T("MT_SpecialistDuplicateInactive");
            if (ParagonUtility.IsParagon(part) && !ParagonMapRegistry.IsEffective(part)) text += "\n\n" + L.T("MT_ParagonInactive");
            return text + (installed ? ShieldModuleStatus(part) : string.Empty);
        }

        private static string ResearchReason(string research) => ResearchDone(research) ? null : L.T("MT_NeedResearch", ResearchLabel(research));
        private static string UpgradeResearchReason(Thing part, int next) => NextIsParagon(part, next)
            ? ResearchReason(ParagonUtility.Settings(part.def).research) ?? ResearchReason("MT_ExoticTurrets")
            : next > 5 ? null : ResearchReason(next >= 4 ? "MT_ExoticTurrets" : "MT_AdvancedTurrets");
        private string ParagonConflictReason(Thing part, int next) => NextIsParagon(part, next) && ParagonMapRegistry.HasUpgradeConflict(turret, part)
            ? L.T("MT_ParagonMapConflict") : null;
        private string UpgradeReason(string slot, int next) => !CanUpgradePart(turret.PartAt(slot)?.def) ? L.T("MT_SpecialistNoUpgrade")
            : ParagonUtility.IsParagon(turret.PartAt(slot)) ? L.T("MT_ParagonMax")
            : !HasNextStage(turret.PartAt(slot), next) ? L.T("MT_MaxTier")
            : PartUpgrade(slot) != null ? L.T("MT_UIR_FinishUpgrade")
            : turret.Orders.Any(o => o.Slot == slot && o.Kind != TurretOperation.UpgradeModule) ? L.T("MT_SlotHasOrder")
            : UpgradeResearchReason(turret.PartAt(slot), next) is string researchReason ? researchReason
            : ParagonConflictReason(turret.PartAt(slot), next) is string conflictReason ? conflictReason
            : turret.Orders.Count >= 100 ? L.T("MT_QueueFull") : null;

        private void DrawAssemblyControls(Rect rect)
        {
            bool narrow = rect.width < 600f;
            bool previousEnabled = GUI.enabled;
            GUI.enabled = previousEnabled && turret.Head?.usesShells == true;
            ControlToggle(new Rect(rect.x, rect.y, Math.Min(rect.width, 220f), 30f), L.T("MT_AutoReloadLabel"), ref turret.AutoReload);
            GUI.enabled = previousEnabled;
            float y = narrow ? rect.y + 34f : rect.y, x = narrow ? rect.x : rect.x + 236f;
            bool core = CombatCompatibility.Provider == null;
            Rect stock = new(x, y + 2f, Math.Max(1f, rect.xMax - x - (core ? 156f : 0f)), 28f);
            Widgets.Label(stock, L.T("MT_MortarAmmoStock", MortarAmmoName(turret.LoadedMortarAmmo ?? turret.SelectedMortarAmmo), turret.LoadedMortarRounds));
            if (core)
            {
                bool allowed = turret.Head?.usesShells == true && turret.Shells < 20 && !turret.Orders.Any(o => o.Kind == TurretOperation.Reload) && turret.Orders.Count < 100
                    && (turret.LoadedMortarRounds == 0 || turret.LoadedMortarAmmo == turret.SelectedMortarAmmo);
                if (ActionButton(new Rect(rect.xMax - 146f, y, 146f, 30f), L.T("MT_ReloadButton"), allowed,
                    turret.LoadedMortarRounds > 0 && turret.LoadedMortarAmmo != turret.SelectedMortarAmmo
                        ? L.T("MT_MortarMixedAmmoReloadBlocked")
                        : L.T("MT_ReloadTip", Math.Max(0, 20 - turret.Shells), MortarAmmoName(turret.SelectedMortarAmmo))) && !turret.QueueReload()) Reject(L.T("MT_ReloadReject"));
            }
            else TooltipHandler.TipRegion(stock, L.T("MT_MortarNativeCEReloadTip"));
            float nextY = y + 34f;
            if (ActionButton(new Rect(rect.x, nextY, Math.Max(1f, rect.width - 154f), 30f),
                L.T("MT_MortarAmmoSelectValue", MortarAmmoName(turret.SelectedMortarAmmo)), turret.Head?.usesShells == true, L.T("MT_MortarAmmoSelectDesc")))
                turret.OpenMortarAmmoMenu();
            if (ActionButton(new Rect(rect.xMax - 146f, nextY, 146f, 30f), L.T("MT_MortarUnloadLabel"),
                turret.CanUnloadMortarAmmo, L.T("MT_MortarUnloadDesc"))) turret.UnloadMortarAmmoFromUI();
        }

        internal static string PartEffectText(Thing part, int tier, bool? paragonPreview = null, Building_ModularTurret context = null)
        {
            PartExtension ext = part.def.GetModExtension<PartExtension>();
            if (ext == null) return L.Description(part.def);
            if (!CanUpgradePart(part.def))
                return TurretPartContext.SpecialistEffects(part, context) + "\n" + L.T("MT_SpecialistNoUpgrade") + "\n" + L.T("MT_UIR_EffectPower", ext.power);
            bool paragon = paragonPreview ?? ParagonMapRegistry.IsEffective(part);
            float power = ParagonUtility.Power(part, tier, paragon);
            bool ce = TurretPartContext.UsesCE;
            bool mortar = ext.isHead ? ext.usesShells : context?.Head?.usesShells == true;
            var lines = new List<string> { TurretPartContext.Header(context) };
            if (ext.isHead)
            {
                lines.Add(L.T("MT_UIR_EffectHead", 1f + 0.2f * tier, 0.05f * tier));
                if (HeadHasGuaranteedHits(ext)) lines.Add(L.T("MT_UIR_EffectGuaranteed"));
                else
                {
                    float missReduction = 1f - HeadAccuracyMissFactor(ext, tier);
                    lines.Add(L.T(ce ? "MT_ContextHeadSway" : "MT_UIR_EffectHeadAccuracy", missReduction));
                    if (!ce && ext.gunDef?.Verbs?.FirstOrDefault()?.ForcedMissRadius > 0f)
                        lines.Add(L.T("MT_UIR_EffectScatter", missReduction));
                }
            }
            switch (ext.moduleKind)
            {
                case "Range": lines.Add(L.T("MT_UIR_EffectRange", RangeBonusPerPoint * power)); break;
                case "Shield":
                    lines.Add(L.T("MT_UIR_EffectShield", ShieldCapacityUtility.Capacity(part, tier, paragon)));
                    lines.Add(L.T("MT_ShieldRechargeRules"));
                    break;
                case "Aiming": lines.Add(L.T("MT_UIR_EffectAiming", 1f - 1f / (1f + 0.1f * power))); break;
                case "Cooling": lines.Add(L.T("MT_UIR_EffectCooling", 1f - 1f / (1f + 0.1f * power))); break;
                case "Auxiliary":
                    var verb = ext.gunDef?.Verbs?.FirstOrDefault();
                    var settings = paragon ? ParagonUtility.Settings(part.def) : null;
                    if (verb != null) lines.Add(L.T("MT_UIR_EffectAuxiliary", (1f + 0.18f * tier) * ParagonUtility.Factor(settings?.auxiliaryDamageMultiplier ?? 1f, 4f),
                        Math.Max(1, Mathf.RoundToInt((verb.burstShotCount + tier) * ParagonUtility.Factor(settings?.auxiliaryBurstMultiplier ?? 1f, 2f))),
                        Math.Max(2, verb.ticksBetweenBurstShots - tier) / 60f));
                    break;
            }
            foreach (var effect in ext.effects ?? new())
            {
                if (effect == null) continue;
                if (effect.workerClass != typeof(ModuleEffect_Stats)) { lines.Add(L.Description(effect)); continue; }
                if (effect.turnSpeedIncreasePerPower != 0f) lines.Add(L.T("MT_UIR_EffectServo", Math.Max(1f, 1f + effect.turnSpeedIncreasePerPower * power)));
                if (effect.accuracyMissReductionPerPower != 0f && (ce || !mortar))
                    lines.Add(L.T(ce ? "MT_ContextSway" : "MT_UIR_EffectAccuracy", 1f - 1f / Math.Max(1f, 1f + effect.accuracyMissReductionPerPower * power)));
                if (!ce && mortar && (effect.mortarScatterReductionBase > 0f || effect.mortarScatterReductionPerTier > 0f))
                    lines.Add(L.T("MT_ContextMortarScatter", 1f - ModuleEffect_Stats.MortarScatterFactor(effect, tier)));
                if (effect.preventFriendlyFire)
                {
                    lines.Add(L.T("MT_ContextFriendlyPawns"));
                    if (mortar) lines.Add(L.T("MT_ContextFriendlyBuildings"));
                }
                if (effect.rangeFactorPerPower != 0f) lines.Add(L.T("MT_UIR_EffectRange", Math.Max(0.01f, 1f + effect.rangeFactorPerPower * power) - 1f));
                if (effect.rangeFlatPerPower != 0f) lines.Add(L.T("MT_UIR_EffectRangeFlat", effect.rangeFlatPerPower * power));
                if (effect.damageFactorPerPower != 0f) lines.Add(L.T("MT_UIR_EffectDamage", Math.Max(0f, 1f + effect.damageFactorPerPower * power)));
                if (effect.armorPerPower != 0f) lines.Add(L.T("MT_UIR_EffectPenetration", effect.armorPerPower * power));
                if (effect.aimingReductionPerPower != 0f) lines.Add(L.T("MT_UIR_EffectAiming", 1f - 1f / Math.Max(1f, 1f + effect.aimingReductionPerPower * power)));
                if (effect.coolingReductionPerPower != 0f) lines.Add(L.T("MT_UIR_EffectCooling", 1f - 1f / Math.Max(1f, 1f + effect.coolingReductionPerPower * power)));
                if (effect.shieldPerPower != 0f) lines.Add(L.T("MT_UIR_EffectShield", effect.shieldPerPower * power));
            }
            if (paragon && tier == 5 && ParagonUtility.Settings(part.def) is { } paragonSettings)
            {
                switch (ext.moduleKind)
                {
                    case "Range": lines.Add(L.T("MT_ParagonEffectRadar", paragonSettings.detectionIntervalTicks / 60f)); break;
                    case "Shield": lines.Add(L.T("MT_ParagonEffectShield", paragonSettings.emergencyShieldFraction,
                        paragonSettings.emergencyShieldCooldownTicks / 60f)); break;
                    case "Auxiliary": lines.Add(L.T("MT_ParagonEffectAuxiliary", paragonSettings.defenseRadius)); break;
                    case "Aiming": lines.Add(L.T("MT_ParagonEffectAiming", paragonSettings.openingArmorBonus * 100f)); break;
                    case "Cooling": lines.Add(L.T("MT_ParagonEffectCooling", 1f - paragonSettings.burstIntervalFactor)); break;
                    case "Servo": lines.Add(L.T("MT_ParagonEffectServo", 1f - paragonSettings.retargetWarmupFactor)); break;
                    case "FireControl":
                        if (!ce && context?.Head?.gunDef?.Verbs?.FirstOrDefault()?.ForcedMissRadius > 0f)
                            lines.Add(L.T("MT_ParagonEffectFireControl", 1f - paragonSettings.forcedMissRadiusFactor));
                        break;
                }
            }
            lines.Add(L.T("MT_UIR_EffectPower", ParagonUtility.PowerConsumption(part, tier, paragon)));
            return string.Join("\n", lines);
        }

        private string MainDamageTip()
        {
            string tip = L.T("MT_UIR_MainDamageTip");
            var verb = turret.AttackVerb;
            if (verb == null) return tip;
            // Read the configured live verb; tier/specialist changes may replace its burst properties.
            string key = CombatCompatibility.Provider != null ? "MT_MainAttackShotsCE"
                : verb is Verb_Tesla ? "MT_MainAttackPulses" : "MT_MainAttackShots";
            return tip + "\n\n" + L.T(key, Math.Max(1, verb.BurstShotCount));
        }

        private void DrawBaseUpgrades(Rect rect)
        {
            DrawMetalPanel(rect, PanelColor, true);
            Widgets.Label(new Rect(rect.x + 12f, rect.y + 7f, rect.width - 24f, 26f), L.T("MT_BaseTitle"));
            TurretOperation[] kinds = { TurretOperation.Damage, TurretOperation.Armor, TurretOperation.Range };
            Rect viewport = new(rect.x + 10f, rect.y + 40f, rect.width - 20f, Math.Max(1f, rect.height - 50f));
            float viewWidth = Math.Max(1f, viewport.width - 20f);
            bool stacked = viewWidth < 760f;
            float width = stacked ? viewWidth : (viewWidth - 20f) / 3f;
            bool researched = ResearchDone("MT_InfiniteEngineering");
            var blocks = kinds.Select(kind =>
            {
                int level = turret.Level(kind), queued = turret.Orders.Count(o => o.Kind == kind), next = level + 1;
                int addedTarget = level + queued + 1;
                var quote = NewBaseUpgradeQuote(addedTarget - 1);
                string effect = kind == TurretOperation.Damage ? L.T("MT_UIR_BaseDamage", 1f + 0.05f * level, 1f + 0.05f * next)
                    : kind == TurretOperation.Armor ? L.T("MT_UIR_BaseArmor", ArmorDamageMultiplier(level), ArmorDamageMultiplier(next))
                    : L.T("MT_UIR_BaseRange", 0.1f * level, 0.1f * next);
                return new[] { OperationLabel(kind), L.T("MT_UIR_BaseCounts", level, queued), effect,
                    L.T("MT_UIR_BaseSingleCost", L.Label(quote.Primary), quote.PrimaryCount, L.Label(quote.Secondary), quote.SecondaryCount,
                        L.Label(quote.Components), quote.ComponentCount, quote.Work),
                    queued > 0 ? L.T("MT_UIR_CostStage", addedTarget) : null };
            }).ToList();
            float cardHeight = blocks.Max(lines => lines.Where(line => !string.IsNullOrEmpty(line)).Sum(line => LabelHeight(line, width - 24f) + 6f)) + 64f;
            Widgets.BeginScrollView(viewport, ref baseScroll, new Rect(0f, 0f, viewWidth, stacked ? 3f * (cardHeight + 10f) : cardHeight));
            for (int i = 0; i < kinds.Length; i++)
            {
                TurretOperation kind = kinds[i];
                float x = stacked ? 0f : i * (width + 10f), y = stacked ? i * (cardHeight + 10f) : 0f;
                DrawMetalPanel(new Rect(x, y, width, cardHeight), CardColor);
                float lineY = y + 12f;
                for (int block = 0; block < blocks[i].Length; block++)
                {
                    if (string.IsNullOrEmpty(blocks[i][block])) continue;
                    var font = Text.Font; var color = GUI.color;
                    if (block == 4) { Text.Font = GameFont.Tiny; GUI.color = new Color(0.76f, 0.80f, 0.82f); }
                    DrawParagraph(x + 12f, ref lineY, width - 24f, blocks[i][block]);
                    Text.Font = font; GUI.color = color;
                }
                Rect one = new(x + 10f, y + cardHeight - 44f, (width - 28f) / 2f, 34f);
                Rect five = new(one.xMax + 8f, one.y, one.width, one.height);
                string reason = researched ? null : ResearchReason("MT_InfiniteEngineering");
                int quotedLevel = turret.Level(kind) + turret.Orders.Count(o => o.Kind == kind);
                string oneReason = reason ?? (turret.Orders.Count >= 100 ? L.T("MT_QueueFull") : null);
                string fiveReason = reason ?? (turret.Orders.Count + 5 > 100 ? L.T("MT_QueueFull") : null);
                if (!BaseUpgradeHasEffect(kind, quotedLevel)) oneReason ??= L.T("MT_BaseArmorLimit");
                if (!BaseUpgradeHasEffect(kind, quotedLevel + 4)) fiveReason ??= L.T("MT_BaseArmorLimit");
                if (ActionButton(one, L.T("MT_UIR_BaseQueueOne"), oneReason == null, oneReason ?? BaseTip(kind, 1))) AddBase(kind, 1);
                if (ActionButton(five, L.T("MT_UIR_BaseQueueFive"), fiveReason == null, fiveReason ?? BaseTip(kind, 5))) AddBase(kind, 5);
            }
            Widgets.EndScrollView();
        }

        private string BaseTip(TurretOperation kind, int count)
        {
            int start = turret.Level(kind) + turret.Orders.Count(o => o.Kind == kind);
            long primary = 0, secondary = 0, components = 0;
            double work = 0;
            var first = NewBaseUpgradeQuote(start);
            for (int i = 0; i < count; i++)
            {
                int n = start + i;
                var quote = NewBaseUpgradeQuote(n);
                primary += quote.PrimaryCount; secondary += quote.SecondaryCount; components += quote.ComponentCount;
                work += quote.Work;
            }
            return L.T("MT_UIR_BaseQueueCost", OperationLabel(kind), count, start + count,
                L.Label(first.Primary), primary, L.Label(first.Secondary), secondary, L.Label(first.Components), components, work);
        }

        private void AddBase(TurretOperation kind, int count)
        {
            if (!ResearchDone("MT_InfiniteEngineering")) { Reject(L.T("MT_NeedResearch", ResearchLabel("MT_InfiniteEngineering"))); return; }
            if (turret.Orders.Count + count > 100) { Reject(L.T("MT_QueueFull")); return; }
            for (int i = 0; i < count; i++)
                if (!turret.QueueBaseUpgrade(kind)) { Reject(L.T("MT_BaseQueueReject")); break; }
        }

        private void DrawQueue(Rect rect)
        {
            DrawMetalPanel(rect, PanelColor, true);
            Rect title = new(rect.x + 12f, rect.y + 8f, rect.width - 24f, 26f);
            if (Prefs.DevMode && turret.IsWrecked && turret.Faction == Faction.OfPlayer)
            {
                Rect supply = new(rect.xMax - 222f, rect.y + 5f, 210f, 30f);
                title.xMax = supply.xMin - 8f;
                if (ActionButton(supply, L.T("MT_WreckSupplyButton"), true, L.T("MT_WreckSupplyTip")))
                    turret.DebugDropWreckRepairMaterials();
            }
            Widgets.Label(title, L.T("MT_QueueTitle", turret.Orders.Count));
            if (turret.Orders.Count == 0)
            {
                Widgets.Label(new Rect(rect.x + 12f, rect.y + 42f, rect.width - 24f, LabelHeight(L.T("MT_QueueEmpty"), rect.width - 24f)), L.T("MT_QueueEmpty"));
                return;
            }
            float top = rect.y + 40f;
            if (turret.AssemblyBlocked)
            {
                string blocked = L.T("MT_AssemblyReturnBlocked");
                float h = Math.Max(34f, LabelHeight(blocked, rect.width - 208f));
                Widgets.Label(new Rect(rect.x + 12f, top, rect.width - 208f, h), blocked);
                if (ActionButton(new Rect(rect.xMax - 184f, top, 172f, 32f), L.T("MT_RetryAssemblyReturn"), true, L.T("MT_AssemblyReturnBlockedDesc")))
                    turret.RetryAssemblyReturn();
                top += h + 8f;
                if (turret.Orders.Count == 0) return;
            }
            Rect viewport = new(rect.x + 10f, top, rect.width - 20f, Math.Max(20f, rect.yMax - top - 10f));
            float viewWidth = Math.Max(1f, viewport.width - 20f), textWidth = Math.Max(1f, viewWidth - 108f);
            var labels = new List<string>(); var costs = new List<string>(); var work = new List<string>(); var heights = new List<float>();
            for (int i = 0; i < turret.Orders.Count; i++)
            {
                TurretOrder order = turret.Orders[i];
                string status = L.T(i > 0 ? "MT_StatusWaiting" : turret.AssemblyBlocked ? "MT_AssemblyReturnBlocked" : !turret.OrderValid(order) ? "MT_StatusInvalid" : turret.CanWork ? "MT_StatusWorking" : "MT_StatusHauling");
                labels.Add(L.T("MT_QueueRow", i + 1, OrderLabel(order), status));
                costs.Add(QueueCosts(order, i));
                work.Add(order.Kind == TurretOperation.Reload
                    ? L.T("MT_ReloadDeliveryProgress", order.ReloadedShells, ReloadShellCount(order))
                    : L.T("MT_UIR_WorkProgress", Math.Clamp(order.WorkTotal - order.WorkLeft, 0f, Math.Max(0f, order.WorkTotal)), order.WorkTotal));
                heights.Add(LabelHeight(labels[i], textWidth) + LabelHeight(costs[i], textWidth) + LabelHeight(work[i], textWidth) + 30f);
            }
            Rect view = new(0f, 0f, viewWidth, heights.Sum() + 6f * heights.Count);
            Widgets.BeginScrollView(viewport, ref queueScroll, view);
            float y = 0f;
            for (int i = 0; i < turret.Orders.Count; i++)
            {
                TurretOrder order = turret.Orders[i];
                Rect row = new(0f, y, view.width, heights[i]);
                float progress = order.Kind == TurretOperation.Reload
                    ? (float)order.ReloadedShells / Math.Max(1, ReloadShellCount(order))
                    : order.WorkTotal <= 0f ? 0f : 1f - order.WorkLeft / order.WorkTotal;
                DrawQueueCard(row, i == 0, textWidth, progress);
                float lineY = row.y + 7f;
                DrawParagraph(8f, ref lineY, textWidth, labels[i]);
                DrawParagraph(8f, ref lineY, textWidth, costs[i]);
                DrawParagraph(8f, ref lineY, textWidth, work[i]);
                TooltipHandler.TipRegion(row, order.Kind == TurretOperation.Reload ? work[i] + "\n" + costs[i]
                    : L.T("MT_QueueTip", Math.Max(0f, order.WorkTotal - order.WorkLeft), order.WorkTotal, costs[i]));
                bool canCancel = turret.CanCancelOrder(i);
                string cancelTip = !canCancel ? L.T("MT_RemovalReturnCannotCancel")
                    : order.Kind is TurretOperation.Damage or TurretOperation.Armor or TurretOperation.Range ? L.T("MT_UIR_CancelBaseOne") : null;
                if (ActionButton(new Rect(row.xMax - 88f, row.y + 12f, 80f, 32f), L.T("MT_CancelButton"), canCancel,
                    cancelTip))
                {
                    if (!turret.CancelOrder(i)) Reject(L.T("MT_NoDropSpace"));
                    break;
                }
                y += row.height + 6f;
            }
            Widgets.EndScrollView();
        }

        private string QueueCosts(TurretOrder order, int index)
        {
            if (order.Kind == TurretOperation.Install)
                return QueuedPartDelivered(order, index) ? L.T("MT_PartDelivered")
                    : L.T("MT_UIR_MaterialsRemaining", L.T("MT_CostPart", L.Label(order.PartDef)));
            if (order.Costs.Count == 0) return L.T("MT_NoCosts");
            // Auto-reload may move a supplied job back in the queue. Allocate
            // staged stock in queue order so it is still shown, but counted once.
            var remaining = order.Costs.Select(c => new
            {
                Def = c.thingDef,
                Count = Math.Max(0, c.count - Math.Max(0,
                    turret.Staged.Where(t => t.def == c.thingDef).Sum(t => t.stackCount)
                    - turret.Orders.Take(index).SelectMany(o => o.Costs).Where(cost => cost.thingDef == c.thingDef).Sum(cost => cost.count)))
            }).Where(c => c.Count > 0).OrderBy(c => MaterialDisplayOrder(c.Def));
            string costs = string.Join(" / ", remaining.Select(c => L.T("MT_CostResource", L.Label(c.Def), c.Count)));
            return string.IsNullOrEmpty(costs) ? L.T("MT_UIR_MaterialsReady") : L.T("MT_UIR_MaterialsRemaining", costs);
        }

        private bool QueuedPartDelivered(TurretOrder order, int index)
        {
            var available = turret.Staged.ToList();
            foreach (var earlier in turret.Orders.Take(index + 1).Where(o => o.Kind == TurretOperation.Install))
            {
                Thing part = available.FirstOrDefault(t => t.def == earlier.PartDef && (earlier.PartId == 0 || t.thingIDNumber == earlier.PartId));
                if (earlier == order) return part != null;
                if (part != null) available.Remove(part);
            }
            return false;
        }

        private void Reject(string message) => Messages.Message(message, turret, MessageTypeDefOf.RejectInput);
    }
}
