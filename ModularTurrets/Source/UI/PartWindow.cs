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
    private sealed partial class AssemblyWindow
    {

        private sealed class PartWindow : Window
        {
            private readonly AssemblyWindow owner;
            private readonly string slot;
            private readonly bool selecting;
            private readonly Thing originalPart;
            private Vector2 scroll;
            private InstallPartCatalog catalog;
            protected override float Margin => 0f;
            public override Vector2 InitialSize
            {
                get
                {
                    float width = Math.Max(1f, Math.Min(selecting ? 760f : 700f, UI.screenWidth - 48f));
                    float height = 740f;
                    if (!selecting)
                    {
                        var oldFont = Text.Font; Text.Font = GameFont.Small;
                        try { height = Math.Max(360f, MeasureUpgrade(width - 60f, out _, out _, out _, out _) + 170f); }
                        finally { Text.Font = oldFont; }
                    }
                    return new Vector2(width, Math.Max(1f, Math.Min(height, UI.screenHeight - 48f)));
                }
            }
            public PartWindow(AssemblyWindow owner, string slot, bool selecting)
            {
                this.owner = owner; this.slot = slot; this.selecting = selecting; originalPart = owner.turret.PartAt(slot);
                doWindowBackground = false; doCloseX = false; draggable = true; absorbInputAroundWindow = true;
                if (selecting) RefreshCandidates();
            }
            public override void PostClose()
            {
                if (owner.partWindow == this) owner.partWindow = null;
                base.PostClose();
            }
            private void RefreshCandidates()
            {
                bool head = slot == "head";
                var definitions = DefDatabase<ThingDef>.AllDefsListForReading
                    .Where(d => d.GetModExtension<PartExtension>() is PartExtension ext && ext.isHead == head
                        && (head || !string.IsNullOrEmpty(ext.moduleKind)) && owner.turret.CanMountPart(slot, d));
                catalog = new InstallPartCatalog(owner.turret, definitions,
                    part => (part.TryGetComp<CompTurretPart>()?.Tier ?? 0, ParagonUtility.IsParagon(part)), Available);
                scroll = Vector2.zero;
            }
            private bool Available(Thing t) => t.Spawned && !t.Destroyed && t.Map == owner.turret.Map && !t.IsForbidden(Faction.OfPlayer) && !t.IsBurning()
                && owner.turret.CanMountPart(slot, t.def)
                && !owner.turret.Orders.Any(o => o.Kind == TurretOperation.Install && o.PartId == t.thingIDNumber);
            public override void DoWindowContents(Rect rect)
            {
                var turret = owner.turret;
                if (!owner.IsOpen || !turret.Spawned || turret.Destroyed || !owner.HasSlot(slot)
                    || turret.PartAt(slot) != originalPart || !selecting && !CanUpgradePart(originalPart?.def)) { Close(); return; }
                var oldFont = Text.Font; var oldAnchor = Text.Anchor; var oldColor = GUI.color;
                try
                {
                    Text.Font = GameFont.Small; Text.Anchor = TextAnchor.UpperLeft; GUI.color = Color.white;
                    DrawConsoleShell(rect);
                    Text.Font = GameFont.Medium;
                    Widgets.Label(new Rect(20f, 16f, rect.width - 80f, 34f), L.T(selecting ? "MT_UIR_SelectTitle" : "MT_UIR_UpgradeTitle"));
                    Text.Font = GameFont.Small;
                    if (ControlButton(new Rect(rect.xMax - 48f, 12f, 32f, 28f), "×")) { Close(); return; }
                    float footerHeight = selecting ? 34f : 76f;
                    Rect footer = new(20f, rect.height - 18f - footerHeight, rect.width - 40f, footerHeight);
                    Rect body = new(20f, 64f, rect.width - 40f, footer.y - 76f);
                    if (selecting)
                    {
                        DrawCandidates(body);
                        if (ControlButton(footer, L.T("MT_UIR_ClosePopup"))) Close();
                    }
                    else DrawUpgrade(body, footer);
                }
                finally { Text.Font = oldFont; Text.Anchor = oldAnchor; GUI.color = oldColor; }
            }
            private void DrawCandidates(Rect rect)
            {
                Widgets.Label(new Rect(rect.x, rect.y, Math.Max(1f, rect.width - 200f), 30f), L.T("MT_UIR_SelectedSlot", SlotLabel(slot)));
                if (ControlButton(new Rect(rect.xMax - 184f, rect.y, 184f, 30f), L.T("MT_UIR_Refresh"))) RefreshCandidates();
                catalog.RefreshAvailability();
                string refreshHint = L.T("MT_InstallCatalogRefresh");
                float hintHeight = LabelHeight(refreshHint, rect.width);
                Widgets.Label(new Rect(rect.x, rect.y + 34f, rect.width, hintHeight), refreshHint);
                var candidates = catalog.Entries.Where(e => e.Base || e.LocalCount + e.OtherMapCount > 0).ToList();
                float top = 42f + hintHeight;
                Rect viewport = new(rect.x, rect.y + top, rect.width, Math.Max(1f, rect.height - top));
                float width = Math.Max(1f, viewport.width - 20f);
                if (candidates.Count == 0) { Widgets.Label(viewport, L.T("MT_NoParts")); return; }
                var locations = candidates.Select(e => e.Stock + "\n" + (e.Item is { } part
                    ? L.T("MT_UIR_PartLocation", part.Position.DistanceTo(owner.turret.Position), part.Position.x, part.Position.z) : e.Status)
                    + (e.OtherMaps == null ? "" : "\n" + e.OtherMaps)).ToList();
                var heights = candidates.Select((e, i) => Math.Max(100f, LabelHeight(L.Label(e.Def), width - 144f)
                    + LabelHeight(e.Grade, width - 112f)
                    + LabelHeight(locations[i], width - 112f) + 24f) + 48f).ToList();
                Widgets.BeginScrollView(viewport, ref scroll, new Rect(0f, 0f, width, heights.Sum() + 8f * candidates.Count));
                float y = 0f;
                for (int i = 0; i < candidates.Count; i++)
                {
                    var candidate = candidates[i];
                    Thing part = candidate.Item ?? (candidate.Preview?.Destroyed == false ? candidate.Preview : null);
                    float height = heights[i];
                    DrawMetalPanel(new Rect(0f, y, width, height), candidate.Item == null ? new Color(0.13f, 0.13f, 0.13f) : CardColor);
                    Rect partIcon = new(10f, y + 10f, 80f, 80f);
                    Color iconColor = GUI.color;
                    if (candidate.Item == null) GUI.color = new Color(0.58f, 0.58f, 0.58f);
                    if (part != null) SlotPartIcons.Draw(partIcon, part, slot);
                    else Widgets.ThingIcon(partIcon, candidate.Def);
                    DrawParagonBadge(partIcon, part);
                    GUI.color = iconColor;
                    TooltipHandler.TipRegion(partIcon, part == null ? TurretPartContext.Description(candidate.Def, owner.turret) : owner.PartDescription(slot, part, installed: false));
                    Widgets.InfoCardButton(width - 32f, y + 10f, candidate.Def);
                    float textY = y + 10f;
                    DrawParagraph(102f, ref textY, width - 144f, L.Label(candidate.Def));
                    DrawParagraph(102f, ref textY, width - 112f, candidate.Grade);
                    DrawParagraph(102f, ref textY, width - 112f, locations[i]);
                    string duplicateReason = owner.turret.HasSpecialistInstallConflict(candidate.Def) ? L.T("MT_SpecialistDuplicate") : null;
                    bool available = duplicateReason == null && owner.turret.PartAt(slot) == null && !owner.turret.Orders.Any(o => o.Slot == slot)
                        && owner.turret.Orders.Count < 100 && candidate.Item != null;
                    if (ActionButton(new Rect(10f, y + height - 42f, width - 20f, 32f),
                        L.T(candidate.Item == null && candidate.LocalCount + candidate.OtherMapCount == 0 ? "MT_InstallCraftNeeded" : "MT_UIR_InstallQueued"), available, duplicateReason ?? locations[i]))
                    {
                        Thing chosen = catalog.Resolve(candidate);
                        if (chosen != null && owner.turret.QueueInstall(slot, chosen)) Close(); else owner.Reject(L.T("MT_PartMoved"));
                        break;
                    }
                    y += height + 8f;
                }
                Widgets.EndScrollView();
            }
            private string StageSummary(int tier, int next)
            {
                TurretOrder active = owner.PartUpgrade(slot);
                return active != null ? L.T("MT_ParagonStageActive", StageLabel(originalPart, tier), owner.UpgradeStatus(active))
                    : ParagonUtility.IsParagon(originalPart) ? L.T("MT_ParagonMax")
                    : NextIsParagon(originalPart, next) ? L.T("MT_ParagonStageNext", tier)
                    : next > 5 ? L.T("MT_UIR_StageMax", tier) : L.T("MT_UIR_StageNext", tier, next);
            }

            private string FollowingEffect(int next) => HasNextStage(originalPart, next)
                ? PartEffectText(originalPart, Math.Min(5, next), NextIsParagon(originalPart, next), owner.turret) : L.T(ParagonUtility.IsParagon(originalPart) ? "MT_ParagonMax" : "MT_MaxTier");

            private string WorkAmount(int next) => L.T("MT_UIR_WorkAmount", NextIsParagon(originalPart, next)
                ? ParagonUtility.Settings(originalPart.def).work : 6000f * next * next * next);

            private string ExtraWarning(int next)
            {
                if (owner.PartUpgrade(slot) != null) return L.T("MT_UIR_FinishUpgrade");
                if (!HasNextStage(originalPart, next) || UpgradeResearchReason(originalPart, next) != null) return null;
                return owner.UpgradeReason(slot, next);
            }

            private List<ThingDefCountClass> UpgradeCosts(int next)
            {
                var costs = ModuleUpgradeCosts.ForUpgrade(originalPart.def, Math.Min(next, 5), NextIsParagon(originalPart, next));
                costs.Sort(static (a, b) => MaterialDisplayOrder(a.thingDef).CompareTo(MaterialDisplayOrder(b.thingDef)));
                return costs;
            }
            private static string ResourceText(ThingDefCountClass cost) => L.T("MT_UIR_ResourceAmount", L.Label(cost.thingDef), cost.count);

            private float MeasureUpgrade(float width, out float intro, out float comparison, out float resourceRow, out float status)
            {
                int tier = originalPart.TryGetComp<CompTurretPart>()?.Tier ?? 0;
                int next = tier + 1;
                intro = Math.Max(72f, LabelHeight(L.Label(originalPart.def), width - 128f)
                    + 6f + LabelHeight(StageSummary(tier, next), width - 128f));
                float column = Math.Max(1f, (width - 42f) / 2f);
                comparison = Math.Max(LabelHeight(PartEffectText(originalPart, tier, context: owner.turret), column),
                    LabelHeight(FollowingEffect(next), column)) + 48f;
                resourceRow = status = 0f;
                float height = intro + 12f + comparison;
                if (HasNextStage(originalPart, next))
                {
                    var costs = UpgradeCosts(next);
                    resourceRow = Math.Max(30f, costs.Select(c => LabelHeight(ResourceText(c), (width - 12f) / 2f - 40f)).DefaultIfEmpty(0f).Max());
                    status = LabelHeight(WorkAmount(next), width);
                    height += 16f + 24f + ((costs.Count + 1) / 2) * (resourceRow + 6f) + 8f + status;
                }
                string warning = ExtraWarning(next);
                if (warning != null) height += 10f + LabelHeight(warning, width);
                return height;
            }

            private void DrawUpgrade(Rect rect, Rect footer)
            {
                var turret = owner.turret;
                int tier = originalPart.TryGetComp<CompTurretPart>()?.Tier ?? 0;
                int next = tier + 1;
                string reason = owner.UpgradeReason(slot, next);
                if (ActionButton(new Rect(footer.x, footer.y, footer.width, 34f),
                    HasNextStage(originalPart, next) ? L.T(NextIsParagon(originalPart, next) ? "MT_ParagonStart" : "MT_UIR_StartUpgrade")
                        : L.T(ParagonUtility.IsParagon(originalPart) ? "MT_ParagonMax" : "MT_MaxTier"), reason == null, reason))
                {
                    if (turret.PartAt(slot) == originalPart && turret.QueueModuleUpgrade(slot)) Close();
                    else owner.Reject(L.T("MT_PartUpgradeReject"));
                    return;
                }
                if (ControlButton(new Rect(footer.x, footer.y + 42f, footer.width, 34f), L.T("MT_UIR_BackToTurret"))) { Close(); return; }
                float width = Math.Max(1f, rect.width - 20f);
                float height = MeasureUpgrade(width, out float intro, out float comparison, out float resourceRow, out float status);
                Widgets.BeginScrollView(rect, ref scroll, new Rect(0f, 0f, width, height));
                Rect icon = new(0f, 0f, 72f, 72f);
                SlotPartIcons.Draw(icon, originalPart, slot);
                DrawParagonBadge(icon, originalPart);
                string info = L.T("MT_UIR_UpgradeInfo", TurretPartContext.Description(originalPart.def, owner.turret), L.T("MT_UIR_EffectScope"));
                if (originalPart.def.GetModExtension<PartExtension>()?.moduleKind == "Shield")
                    info += "\n\n" + L.T("MT_ShieldRechargeRules") + owner.ShieldModuleStatus(originalPart);
                if (NextIsParagon(originalPart, next) || ParagonUtility.IsParagon(originalPart)) info += "\n\n" + L.T("MT_ParagonInfo");
                TooltipHandler.TipRegion(icon, info);
                float nameY = 0f;
                DrawParagraph(88f, ref nameY, width - 128f, L.Label(originalPart.def));
                DrawParagraph(88f, ref nameY, width - 128f, StageSummary(tier, next));
                Rect information = new(width - 30f, 2f, 30f, 28f);
                DrawMetalPanel(information, Mouse.IsOver(information) ? new Color(0.25f, 0.34f, 0.35f) : CardColor);
                Widgets.DrawBoxSolid(new Rect(information.x + 2f, information.y + 2f, 2f, information.height - 4f), AccentColor);
                Text.Anchor = TextAnchor.MiddleCenter; Widgets.Label(information, L.T("MT_InfoMark")); Text.Anchor = TextAnchor.UpperLeft;
                TooltipHandler.TipRegion(information, info);
                float y = intro + 12f;
                DrawMetalPanel(new Rect(0f, y, width, comparison), PanelColor);
                float column = Math.Max(1f, (width - 42f) / 2f), rightX = width / 2f + 9f;
                Widgets.DrawBoxSolid(new Rect(width / 2f, y + 12f, 1f, comparison - 24f), EdgeColor);
                Widgets.Label(new Rect(12f, y + 10f, column, 24f), ParagonUtility.IsParagon(originalPart)
                    ? L.T(ParagonMapRegistry.IsEffective(originalPart) ? "MT_ParagonCurrent" : "MT_ParagonCurrentRestricted") : L.T("MT_UIR_CompareCurrent", tier));
                Color previous = GUI.color; GUI.color = AccentColor;
                Widgets.Label(new Rect(rightX, y + 10f, column, 24f),
                    NextIsParagon(originalPart, next) ? L.T("MT_ParagonNext")
                        : HasNextStage(originalPart, next) ? L.T("MT_UIR_CompareNext", next) : L.T("MT_MaxTier"));
                GUI.color = previous;
                Widgets.Label(new Rect(12f, y + 38f, column, comparison - 48f), PartEffectText(originalPart, tier, context: owner.turret));
                Widgets.Label(new Rect(rightX, y + 38f, column, comparison - 48f), FollowingEffect(next));
                y += comparison;
                if (HasNextStage(originalPart, next))
                {
                    y += 16f;
                    Widgets.Label(new Rect(0f, y, width, 24f), L.T("MT_UIR_RequiredMaterials"));
                    y += 24f;
                    var costs = UpgradeCosts(next);
                    float resourceWidth = (width - 12f) / 2f;
                    for (int i = 0; i < costs.Count; i++)
                    {
                        float x = (i % 2) * (resourceWidth + 12f), rowY = y + (i / 2) * (resourceRow + 6f);
                        ThingDef def = costs[i].thingDef;
                        Widgets.ThingIcon(new Rect(x, rowY, 28f, 28f), def);
                        Widgets.Label(new Rect(x + 38f, rowY, resourceWidth - 40f, resourceRow), ResourceText(costs[i]));
                    }
                    y += ((costs.Count + 1) / 2) * (resourceRow + 6f) + 8f;
                    GUI.color = new Color(0.76f, 0.80f, 0.82f);
                    Widgets.Label(new Rect(0f, y, width, status), WorkAmount(next));
                    GUI.color = previous;
                    y += status;
                }
                string warning = ExtraWarning(next);
                if (warning != null)
                {
                    GUI.color = AmberColor;
                    Widgets.Label(new Rect(0f, y + 10f, width, LabelHeight(warning, width)), warning);
                    GUI.color = previous;
                }
                Widgets.EndScrollView();
            }

        }
    }
}
