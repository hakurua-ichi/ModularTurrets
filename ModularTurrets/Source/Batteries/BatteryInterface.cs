using System;
using System.Collections.Generic;
using System.Text;
using RimWorld;
using UnityEngine;
using Verse;
using static ModularTurrets.AssemblyConsole;

namespace ModularTurrets.Batteries;

public sealed partial class Building_ModularBattery
{
    public override IEnumerable<Gizmo> GetGizmos()
    {
        foreach (Gizmo gizmo in base.GetGizmos()) yield return gizmo;
        if (Faction != Faction.OfPlayer) yield break;
        yield return new Command_Action
        {
            defaultLabel = L.T("MT_BatteryManage"),
            defaultDesc = L.T("MT_BatteryManageDesc"),
            icon = ContentFinder<Texture2D>.Get("ModularTurrets/UI/Manage"),
            action = () => Find.WindowStack.Add(new BatteryWindow(this))
        };
    }

    public override string GetInspectString()
    {
        var text = new StringBuilder(base.GetInspectString()?.TrimEnd());
        var power = Battery;
        if (power?.ReserveCapacity > 0f)
            text.AppendLine().Append(L.T("MT_BatteryReserveStatus", power.ReserveEnergy, power.ReserveCapacity));
        if (power?.GenerationWatts > 0f)
            text.AppendLine().Append(L.T("MT_BatteryGenerationStatus", power.GenerationWatts));
        if (power?.WirelessRadius > 0f)
            text.AppendLine().Append(L.T("MT_BatteryWirelessStatus", power.WirelessRadius));
        foreach (string slot in BatteryWindow.Slots)
            text.AppendLine().Append(BatteryWindow.SlotName(slot)).Append(": ").Append(ModuleAt(slot) is Thing part ? ModuleLabel(part) : L.T("MT_BatteryEmpty"));
        return text.ToString().Trim();
    }

    private static string ModuleLabel(Thing item)
    {
        if (ModularTurretsMod.Instance?.Settings?.CurrentLanguage is null or "auto") return item.LabelCap.ToString();
        string name = L.Label(item.def);
        return item.TryGetComp<CompBatteryModuleUpgrade>()?.TransformLabel(name) ?? name;
    }

    private sealed class BatteryWindow : Window
    {
        internal static readonly string[] Slots = { "left", "right" };
        private readonly Building_ModularBattery battery;
        private readonly Texture bodyTexture, glass;
        private readonly List<UpgradeCard> upgradeCards = new(3);
        private Vector2 queueScroll, upgradesScroll;
        private bool upgradesTab;
        private float queueContentHeight;
        private string selected = "base";
        private BatterySlotWindow slotWindow;

        protected override float Margin => 0f;
        public override Vector2 InitialSize => WindowSize;

        internal BatteryWindow(Building_ModularBattery battery)
        {
            this.battery = battery;
            bodyTexture = battery.Graphic.MatSingle.mainTexture;
            glass = ContentFinder<Texture2D>.Get("ModularTurrets/Batteries/ChargeGlass");
            doCloseX = false;
            doWindowBackground = false;
            draggable = true;
            absorbInputAroundWindow = true;
        }

        internal static string SlotName(string slot) => L.T(slot switch
        {
            "left" => "MT_BatteryLeft", "right" => "MT_BatteryRight", _ => "MT_BatteryBody"
        });

        public override void DoWindowContents(Rect rect)
        {
            if (!battery.Spawned || battery.Destroyed || battery.Faction != Faction.OfPlayer) { Close(); return; }
            var oldFont = Text.Font; var oldAnchor = Text.Anchor; var oldColor = GUI.color;
            bool oldEnabled = GUI.enabled;
            try
            {
                Text.Font = GameFont.Small; Text.Anchor = TextAnchor.UpperLeft; GUI.color = Color.white;
                var power = battery.Battery;
                string status = L.T("MT_BatteryStats", power.StoredEnergy, battery.Capacity, battery.ChargeEfficiency * 100f);
                if (DrawHeader(rect, L.T("MT_BatteryTitle"), Mathf.CeilToInt(power.ChargeFraction * 5f), AccentColor, status))
                { Close(); return; }

                rect = rect.ContractedBy(20f);
                float y = rect.y + 42f;
                DrawParagraph(rect.x, ref y, rect.width, status);
                if (power.ReserveCapacity > 0f)
                    DrawParagraph(rect.x, ref y, rect.width, L.T("MT_BatteryReserveStatus", power.ReserveEnergy, power.ReserveCapacity));

                float half = (rect.width - 8f) / 2f;
                if (ControlButton(new Rect(rect.x, y, half, 32f), L.T("MT_UIR_PartsTab"), active: !upgradesTab)) upgradesTab = false;
                if (ControlButton(new Rect(rect.x + half + 8f, y, half, 32f), L.T("MT_UIR_UpgradePart"), active: upgradesTab)) upgradesTab = true;
                y += 42f;
                float queueHeight = Math.Min(200f, Math.Max(90f, (rect.yMax - y) * 0.28f));
                Rect queue = new(rect.x, rect.yMax - queueHeight, rect.width, queueHeight);
                Rect body = new(rect.x, y, rect.width, Math.Max(1f, queue.y - y - 10f));
                if (upgradesTab) DrawUpgrades(body);
                else DrawPreview(body, power.ChargeFraction);
                DrawQueue(queue);
            }
            finally { Text.Font = oldFont; Text.Anchor = oldAnchor; GUI.color = oldColor; GUI.enabled = oldEnabled; }
        }

        public override void PostClose()
        {
            slotWindow?.Close();
            base.PostClose();
        }

        private void OpenSlot(string slot)
        {
            if (slotWindow?.IsOpen == true) return;
            selected = slot;
            slotWindow = new BatterySlotWindow(this, slot);
            Find.WindowStack.Add(slotWindow);
        }

        private void DrawPreview(Rect panel, float fraction)
        {
            DrawMetalPanel(panel, PanelColor, true);
            Rect inner = panel.ContractedBy(10f);
            float gap = Math.Clamp(inner.width * 0.06f, 8f, 36f);
            float width = Math.Max(1f, (inner.width - 2f * gap) / 3f);
            float height = Math.Max(1f, Math.Min(inner.height, Math.Min(322f, width * 1.7f + 70f)));
            Rect left = new(inner.x, inner.center.y - height / 2f, width, height);
            Rect center = new(left.xMax + gap, left.y, width, height);
            Rect right = new(center.xMax + gap, left.y, width, height);
            Thing leftPart = battery.ModuleAt("left"), rightPart = battery.ModuleAt("right");

            Widgets.DrawLine(left.center, center.center, leftPart == null ? EdgeColor : AccentColor, 2f);
            Widgets.DrawLine(center.center, right.center, rightPart == null ? EdgeColor : AccentColor, 2f);
            DrawNode(left, "left", leftPart, fraction);
            DrawNode(center, "base", null, fraction);
            DrawNode(right, "right", rightPart, fraction);
        }

        private void DrawNode(Rect card, string slot, Thing part, float fraction)
        {
            bool body = slot == "base", active = selected == slot;
            DrawMetalPanel(card, active ? new Color(0.20f, 0.27f, 0.28f) : CardColor);
            Widgets.DrawBoxSolid(new Rect(card.x + 2f, card.y + 3f, 3f, card.height - 6f), active ? AccentColor : EdgeColor);
            string title = SlotName(slot);
            string name = body ? L.T("MT_BatteryLevel", battery.CapacityLevel) : part == null ? L.T("MT_UIR_EmptySlot") : ModuleLabel(part);
            Rect inner = card.ContractedBy(Math.Min(8f, card.width * 0.07f));
            bool compact = inner.height < 160f || inner.width < 88f;
            float heading = Math.Min(LabelHeight(title, inner.width), Math.Max(18f, inner.height * 0.28f));
            Rect caption = new(inner.x, inner.y, inner.width, heading);
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(caption, title);
            Rect action = new(inner.x, inner.yMax - 30f, inner.width, 30f);
            float nameHeight = compact ? 0f : Math.Min(LabelHeight(name, inner.width), inner.height * 0.23f);
            Rect label = new(inner.x, action.y - nameHeight - 4f, inner.width, nameHeight);
            float iconBottom = compact ? inner.yMax : label.y - 3f;
            Rect icon = new(inner.x + 2f, caption.yMax + 4f, Math.Max(1f, inner.width - 4f), Math.Max(1f, iconBottom - caption.yMax - 4f));
            Widgets.DrawBoxSolid(icon, RecessColor);
            float artHeight = Math.Max(1f, Math.Min(icon.height - 8f, (icon.width - 8f) * 3f / Graphic_ModularBattery.ModuleWidth));
            float artWidth = artHeight * Graphic_ModularBattery.ModuleWidth / 3f;
            Rect art = new(icon.center.x - artWidth / 2f, icon.center.y - artHeight / 2f, artWidth, artHeight);
            if (body) DrawBody(art, fraction);
            else if (part == null)
            {
                Text.Font = GameFont.Medium; Widgets.Label(icon, L.T("MT_EmptySlotMark")); Text.Font = GameFont.Small;
            }
            else if (part.Graphic.MatSingle.mainTexture is Texture texture)
            {
                if (slot == "right") GUI.DrawTextureWithTexCoords(art, texture, new Rect(1f, 0f, -1f, 1f));
                else GUI.DrawTexture(art, texture);
            }
            if (!compact) Widgets.Label(label, name);
            Text.Anchor = TextAnchor.UpperLeft;
            TooltipHandler.TipRegion(icon, title + "\n" + name);
            Rect selection = card;
            if (!compact) selection.yMax = action.y;
            if (Widgets.ButtonInvisible(selection)) OpenSlot(slot);
            if (compact) return;
            if (body || part == null)
            {
                if (ControlButton(action, L.T(body ? "MT_PartDetailsButton" : "MT_BatteryInstall"))) OpenSlot(slot);
                return;
            }
            float buttonWidth = (action.width - 6f) / 2f;
            if (ControlButton(new Rect(action.x, action.y, buttonWidth, action.height), L.T("MT_PartDetailsButton")))
                OpenSlot(slot);
            if (ControlButton(new Rect(action.x + buttonWidth + 6f, action.y, buttonWidth, action.height),
                L.T("MT_BatteryRemove"), battery.CanQueue && !battery.HasSlotOrder(slot)))
            {
                if (!battery.QueueRemove(slot))
                    Messages.Message(L.T("MT_BatteryRequestFailed"), MessageTypeDefOf.RejectInput, false);
            }
        }

        private void DrawBody(Rect art, float fraction)
        {
            // The center third of the assembled texture is the 1x3 body.
            const float span = Graphic_ModularBattery.ModuleWidth / 3f;
            GUI.DrawTextureWithTexCoords(art, bodyTexture, new Rect((1f - span) / 2f, 0f, span, 1f));
            if (fraction <= 0f) return;
            Rect tube = new(art.center.x - art.width * 78f / 590f, art.y + art.height * (501f - 66f) / 2041f,
                art.width * 156f / 590f, art.height * 1173f / 2041f);
            tube.yMin = tube.yMax - tube.height * fraction;
            GUI.DrawTextureWithTexCoords(tube, glass, new Rect(0f, 0f, 1f, fraction));
        }

        private readonly record struct UpgradeCard(string Slot, string Title, string Stage, string Effect, string Cost,
            string Button, bool Enabled, string Tip)
        {
            internal float Height(float width)
            {
                float textWidth = Math.Max(1f, width - 24f);
                return LabelHeight(Title, textWidth) + LabelHeight(Stage, textWidth) + LabelHeight(Effect, textWidth)
                    + (Cost != null ? LabelHeight(Cost, textWidth) + 6f : 0f) + 86f;
            }
        }

        private UpgradeCard BaseUpgrade()
        {
            int queued = 0;
            foreach (var order in battery.AssemblyOrders)
                if (order.Kind == BatteryOperation.Upgrade) queued++;
            var settings = battery.Settings;
            float upgradeFactor = 1f;
            foreach (string slot in Slots)
            {
                float bonus = battery.ModuleAt(slot)?.def.GetModExtension<BatteryModuleExtension>()?.upgradeCapacityBonusFactor ?? 0f;
                if (float.IsFinite(bonus) && bonus > 0f) upgradeFactor += bonus;
            }
            return new("base", L.T("MT_BatteryBody"), L.T("MT_UIR_BaseCounts", battery.CapacityLevel, queued),
                L.T("MT_BatteryCapacityGain", battery.Capacity, Positive(settings?.capacityPerUpgrade ?? 1200f, 1200f) * upgradeFactor),
                L.T("MT_BatteryUpgradeCost", settings?.upgradeSteel > 0 ? settings.upgradeSteel : 80,
                    settings?.upgradeComponents > 0 ? settings.upgradeComponents : 2, Positive(settings?.upgradeWork ?? 600f, 600f)),
                L.T("MT_BatteryUpgrade"), battery.CanQueueUpgrade, L.T("MT_BatteryUpgradeDesc"));
        }

        private void AddModuleUpgrade(string slot)
        {
            int max = battery.ModuleUpgradeMaxLevel(slot);
            var part = battery.ModuleAt(slot);
            if (max <= 0 || part?.def.GetModExtension<BatteryModuleExtension>() is not { } ext) return;
            int level = battery.ModuleUpgradeLevel(slot);
            bool hasNext = battery.TryGetModuleUpgradeQuote(slot, out int target, out int steel, out int components, out float work);
            string effect;
            if (ext.wirelessRadiusPerLevel > 0f)
            {
                float radius = ext.WirelessRadiusAtLevel(level);
                effect = hasNext ? L.T("MT_BatteryCoilUpgradeEffect", radius, ext.WirelessRadiusAtLevel(target))
                    : L.T("MT_BatteryWirelessStatus", radius);
            }
            else
            {
                float watts = ext.GenerationWattsAtLevel(battery.Capacity, level);
                effect = hasNext ? L.T("MT_BatteryGenerationUpgradeEffect", watts, ext.GenerationWattsAtLevel(battery.Capacity, target))
                    : L.T("MT_BatteryGenerationStatus", watts);
            }
            bool pending = battery.HasSlotOrder(slot);
            string stage = L.T("MT_BatteryModuleStage", level, max);
            if (pending) stage += "\n" + L.T("MT_UIR_SlotPending");
            upgradeCards.Add(new(slot, L.T("MT_BatteryInstalledPart", SlotName(slot), L.Label(part.def)), stage, effect,
                hasNext ? L.T("MT_BatteryUpgradeCost", steel, components, work) : null,
                hasNext ? L.T("MT_UIR_StartUpgrade") : L.T("MT_MaxTier"), hasNext && battery.CanQueueModuleUpgrade(slot), null));
        }

        private void DrawUpgrades(Rect panel)
        {
            DrawMetalPanel(panel, PanelColor, true);
            upgradeCards.Clear();
            upgradeCards.Add(BaseUpgrade());
            foreach (string slot in Slots) AddModuleUpgrade(slot);
            Rect port = panel.ContractedBy(10f);
            float viewWidth = Math.Max(1f, port.width - 18f);
            const float gap = 10f;
            bool stacked = (viewWidth - gap * (upgradeCards.Count - 1)) / upgradeCards.Count < 235f;
            float cardWidth = stacked ? viewWidth : (viewWidth - gap * (upgradeCards.Count - 1)) / upgradeCards.Count;
            float height = 0f;
            foreach (var card in upgradeCards) height = Math.Max(height, card.Height(cardWidth));
            float contentHeight = stacked ? upgradeCards.Count * (height + gap) - gap : height;
            Widgets.BeginScrollView(port, ref upgradesScroll, new Rect(0f, 0f, viewWidth, Math.Max(port.height, contentHeight)));
            try
            {
                for (int i = 0; i < upgradeCards.Count; i++)
                {
                    var card = upgradeCards[i];
                    Rect rect = new(stacked ? 0f : i * (cardWidth + gap), stacked ? i * (height + gap) : 0f, cardWidth, height);
                    DrawMetalPanel(rect, CardColor);
                    float y = rect.y + 12f, x = rect.x + 12f, width = Math.Max(1f, rect.width - 24f);
                    DrawParagraph(x, ref y, width, card.Title);
                    DrawParagraph(x, ref y, width, card.Stage);
                    DrawParagraph(x, ref y, width, card.Effect);
                    if (card.Cost != null) DrawParagraph(x, ref y, width, card.Cost);
                    if (ActionButton(new Rect(x, rect.yMax - 44f, width, 32f), card.Button, card.Enabled, card.Tip))
                    {
                        bool accepted = card.Slot == "base" ? battery.QueueUpgrade() : battery.QueueModuleUpgrade(card.Slot);
                        if (!accepted) Messages.Message(L.T("MT_BatteryRequestFailed"), battery, MessageTypeDefOf.RejectInput);
                    }
                }
            }
            finally { Widgets.EndScrollView(); }
        }

        private sealed class BatterySlotWindow : Window
        {
            private readonly BatteryWindow owner;
            private readonly Building_ModularBattery battery;
            private readonly string slot;
            private readonly Thing originalPart;
            private InstallPartCatalog catalog;
            private Vector2 scroll;
            private float contentHeight;

            protected override float Margin => 0f;
            public override Vector2 InitialSize => new(Math.Max(1f, Math.Min(720f, UI.screenWidth - 48f)),
                Math.Max(1f, Math.Min(slot == "base" || originalPart != null ? 380f : 680f, UI.screenHeight - 48f)));

            internal BatterySlotWindow(BatteryWindow owner, string slot)
            {
                this.owner = owner;
                battery = owner.battery;
                this.slot = slot;
                originalPart = battery.ModuleAt(slot);
                doWindowBackground = false; doCloseX = false; draggable = true; absorbInputAroundWindow = true;
                if (slot != "base" && originalPart == null) RefreshCandidates();
            }

            public override void PostClose()
            {
                if (owner.slotWindow == this) owner.slotWindow = null;
                base.PostClose();
            }

            public override void DoWindowContents(Rect rect)
            {
                if (!owner.IsOpen || !battery.Spawned || battery.Destroyed || battery.Faction != Faction.OfPlayer
                    || battery.ModuleAt(slot) != originalPart) { Close(); return; }
                var font = Text.Font; var anchor = Text.Anchor; var color = GUI.color;
                bool wrap = Text.WordWrap;
                try
                {
                    Text.Font = GameFont.Small; Text.Anchor = TextAnchor.UpperLeft; Text.WordWrap = true; GUI.color = Color.white;
                    DrawConsoleShell(rect);
                    Text.Font = GameFont.Medium;
                    Widgets.Label(new Rect(20f, 16f, rect.width - 80f, 34f), SlotName(slot));
                    Text.Font = GameFont.Small;
                    if (ControlButton(new Rect(rect.xMax - 48f, 12f, 32f, 28f), "×")) { Close(); return; }
                    Rect footer = new(20f, rect.yMax - 52f, rect.width - 40f, 34f);
                    Rect port = new(20f, 64f, rect.width - 40f, Math.Max(1f, footer.y - 76f));
                    float width = Math.Max(1f, port.width - 18f);
                    Widgets.BeginScrollView(port, ref scroll, new Rect(0f, 0f, width, Math.Max(port.height, contentHeight)));
                    try
                    {
                        float y = 0f;
                        if (slot == "base") DrawBodyInfo(width, ref y);
                        else DrawParts(width, ref y);
                        contentHeight = y;
                    }
                    finally { Widgets.EndScrollView(); }
                    if (ControlButton(footer, L.T("MT_UIR_BackToTurret"))) Close();
                }
                finally { Text.Font = font; Text.Anchor = anchor; Text.WordWrap = wrap; GUI.color = color; }
            }

            private void DrawBodyInfo(float width, ref float y)
            {
                var power = battery.Battery;
                DrawParagraph(0f, ref y, width, L.T("MT_BatteryLevel", battery.CapacityLevel));
                DrawParagraph(0f, ref y, width, L.T("MT_BatteryStats", power.StoredEnergy, battery.Capacity, battery.ChargeEfficiency * 100f));
                if (power.ReserveCapacity > 0f)
                    DrawParagraph(0f, ref y, width, L.T("MT_BatteryReserveStatus", power.ReserveEnergy, power.ReserveCapacity));
                if (power.GenerationWatts > 0f)
                    DrawParagraph(0f, ref y, width, L.T("MT_BatteryGenerationStatus", power.GenerationWatts));
                if (power.WirelessRadius > 0f)
                    DrawParagraph(0f, ref y, width, L.T("MT_BatteryWirelessStatus", power.WirelessRadius));
                DrawParagraph(0f, ref y, width, L.Description(battery.def));
            }

            private void DrawParts(float width, ref float y)
            {
                var installed = battery.ModuleAt(slot);
                bool pending = battery.HasSlotOrder(slot);
                if (pending) DrawParagraph(0f, ref y, width, L.T("MT_UIR_SlotPending"));
                if (installed != null)
                {
                    DrawPartCard(width, ref y, installed.def, installed, installed: true, enabled: !pending && battery.CanQueue);
                    return;
                }

                if (ControlButton(new Rect(0f, y, width, 30f), L.T("MT_UIR_Refresh"))) RefreshCandidates();
                y += 38f;
                DrawParagraph(0f, ref y, width, L.T("MT_InstallCatalogRefresh"));
                catalog.RefreshAvailability();
                foreach (var candidate in catalog.Entries)
                {
                    if (!candidate.Base && candidate.LocalCount + candidate.OtherMapCount == 0) continue;
                    DrawPartCard(width, ref y, candidate.Def, candidate.Item, installed: false,
                        enabled: candidate.Item != null && !pending && battery.CanQueue, candidate);
                }
            }

            private void DrawPartCard(float width, ref float y, ThingDef part, Thing item, bool installed, bool enabled, InstallPartCatalog.Entry candidate = null)
            {
                string name = candidate != null ? L.Label(part) + " · " + candidate.Grade : item == null ? L.Label(part) : ModuleLabel(item);
                string desc = L.Description(part) + (candidate == null ? "" : "\n\n" + candidate.Stock
                    + (candidate.Status == null ? "" : "\n" + candidate.Status)
                    + (candidate.OtherMaps == null ? "" : "\n" + candidate.OtherMaps));
                string label = L.T(installed ? "MT_BatteryRemove" : candidate != null && candidate.LocalCount + candidate.OtherMapCount == 0
                    ? "MT_InstallCraftNeeded" : "MT_BatteryInstall");
                float icon = Math.Min(width * 0.18f, 66f);
                float textX = 12f + icon, textWidth = Math.Max(1f, width - textX - 8f);
                float nameHeight = LabelHeight(name, Math.Max(1f, textWidth - 28f)), descHeight = LabelHeight(desc, textWidth);
                float rowHeight = Math.Max(icon + 16f, nameHeight + descHeight + 18f) + 40f;
                Rect row = new(0f, y, width, rowHeight);
                DrawMetalPanel(row, candidate != null && candidate.Item == null ? new Color(0.13f, 0.13f, 0.13f) : CardColor);
                Color iconColor = GUI.color;
                if (candidate != null && candidate.Item == null) GUI.color = new Color(0.58f, 0.58f, 0.58f);
                if (part.graphicData?.Graphic.MatSingle.mainTexture is Texture texture)
                    GUI.DrawTexture(new Rect(6f, y + 8f, icon, icon), texture, ScaleMode.ScaleToFit);
                GUI.color = iconColor;
                Widgets.Label(new Rect(textX, y + 7f, Math.Max(1f, textWidth - 28f), nameHeight), name);
                Widgets.InfoCardButton(width - 30f, y + 7f, part);
                Widgets.Label(new Rect(textX, y + 9f + nameHeight, textWidth, descHeight), desc);
                Rect action = new(8f, row.yMax - 38f, width - 16f, 30f);
                if (ActionButton(action, label, enabled, enabled ? null : battery.HasSlotOrder(slot) ? L.T("MT_UIR_SlotPending")
                    : !battery.CanQueue ? L.T("MT_BatteryRequestFailed") : candidate?.Status ?? L.T("MT_BatteryNoModules")))
                {
                    Thing chosen = installed ? null : catalog.Resolve(candidate);
                    bool accepted = installed ? battery.QueueRemove(slot) : chosen != null && battery.QueueInstall(slot, chosen);
                    Complete(accepted);
                }
                y = row.yMax + 6f;
            }

            private void RefreshCandidates()
            {
                var definitions = new List<ThingDef>();
                foreach (var def in DefDatabase<ThingDef>.AllDefsListForReading)
                    if (IsModule(def)) definitions.Add(def);
                catalog = new InstallPartCatalog(battery, definitions,
                    part => (part.TryGetComp<CompBatteryModuleUpgrade>()?.Level ?? 0, false), Available);
                scroll = Vector2.zero;
            }

            private bool Available(Thing item) => item is { Spawned: true } && item.Map == battery.Map
                && !item.IsForbidden(Faction.OfPlayer) && !item.IsBurning();

            private void Complete(bool accepted)
            {
                if (accepted) Close();
                else Messages.Message(L.T("MT_BatteryRequestFailed"), MessageTypeDefOf.RejectInput, false);
            }
        }

        private void DrawQueue(Rect panel)
        {
            DrawMetalPanel(panel, PanelColor, true);
            Rect area = panel.ContractedBy(10f);
            var orders = battery.AssemblyOrders;
            Rect title = new(area.x, area.y, area.width, 28f);
            if (battery.AssemblyBlocked)
            {
                title.xMax -= 144f;
                if (ActionButton(new Rect(title.xMax + 8f, area.y, 136f, 28f), L.T("MT_BatteryRetry"), true, L.T("MT_AssemblyReturnBlockedDesc")))
                    battery.RetryAssemblyReturn();
            }
            Widgets.Label(title, L.T("MT_BatteryQueueCount", orders.Count));
            area.yMin += 34f;
            if (orders.Count == 0)
            {
                Widgets.Label(area, L.T("MT_BatteryNoOrders"));
                queueScroll = Vector2.zero;
                queueContentHeight = 0f;
                return;
            }
            float width = Math.Max(1f, area.width - 18f);
            Widgets.BeginScrollView(area, ref queueScroll, new Rect(0f, 0f, width, Math.Max(area.height, queueContentHeight)));
            try
            {
                float y = 0f, textWidth = Math.Max(1f, width - 120f);
                for (int i = 0; i < orders.Count; i++)
                {
                    BatteryOrder order = orders[i];
                    string status = L.T(i > 0 ? "MT_StatusWaiting" : battery.AssemblyBlocked ? "MT_AssemblyReturnBlocked"
                        : !battery.OrderValid(order) ? "MT_StatusInvalid" : battery.CanWork ? "MT_StatusWorking" : "MT_StatusHauling");
                    string titleText = L.T("MT_QueueRow", i + 1, OrderLabel(order), status);
                    string work = L.T("MT_UIR_WorkProgress", Math.Max(0f, order.WorkTotal - order.WorkLeft), order.WorkTotal);
                    string materials = MaterialStatus(order, i);
                    Rect row = new(0f, y, width, 20f + LabelHeight(titleText, textWidth) + LabelHeight(work, textWidth)
                        + (materials.Length > 0 ? LabelHeight(materials, textWidth) + 6f : 0f) + 6f);
                    float progress = order.WorkTotal > 0f ? 1f - order.WorkLeft / order.WorkTotal : 0f;
                    DrawQueueCard(row, i == 0, textWidth, progress);
                    float line = row.y + 5f;
                    DrawParagraph(8f, ref line, textWidth, titleText);
                    if (materials.Length > 0) DrawParagraph(8f, ref line, textWidth, materials);
                    DrawParagraph(8f, ref line, textWidth, work);
                    if (ActionButton(new Rect(row.xMax - 100f, row.y + 10f, 92f, 32f), L.T("MT_BatteryCancel"), true, null))
                    {
                        if (!battery.CancelOrder(order.Id)) Messages.Message(L.T("MT_NoDropSpace"), battery, MessageTypeDefOf.RejectInput);
                        break;
                    }
                    y = row.yMax + 6f;
                }
                queueContentHeight = y;
            }
            finally { Widgets.EndScrollView(); }
        }

        private string MaterialStatus(BatteryOrder order, int index)
        {
            if (order.Kind == BatteryOperation.Install)
                return index == 0 && battery.Needed(order.PartDef) == 0 ? L.T("MT_PartDelivered") : L.T("MT_CostPart", L.Label(order.PartDef));
            if (order.Costs.Count == 0) return string.Empty;
            var items = new StringBuilder();
            foreach (var cost in order.Costs)
            {
                if (cost?.thingDef == null) continue;
                int count = index == 0 ? battery.Needed(cost.thingDef) : cost.count;
                if (count <= 0) continue;
                if (items.Length > 0) items.Append(" / ");
                items.Append(L.T("MT_UIR_ResourceAmount", L.Label(cost.thingDef), count));
            }
            return items.Length == 0 ? L.T("MT_UIR_MaterialsReady") : L.T("MT_UIR_MaterialsRemaining", items.ToString());
        }

        private static string OrderLabel(BatteryOrder order) => order.Kind switch
        {
            BatteryOperation.Install => L.T("MT_BatteryOrderInstall", SlotName(order.Slot), L.Label(order.PartDef)),
            BatteryOperation.Remove => L.T("MT_BatteryOrderRemove", SlotName(order.Slot)),
            BatteryOperation.UpgradeModule => L.T("MT_BatteryOrderUpgradeModule", SlotName(order.Slot), L.Label(order.PartDef), order.TargetLevel),
            _ => L.T("MT_BatteryOrderUpgrade")
        };

    }
}
