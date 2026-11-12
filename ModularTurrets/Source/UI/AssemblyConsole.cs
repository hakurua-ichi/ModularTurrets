using System;
using UnityEngine;
using Verse;

namespace ModularTurrets;

internal static class AssemblyConsole
{
    internal static readonly Color PanelColor = new(0.105f, 0.125f, 0.14f);
    internal static readonly Color CardColor = new(0.17f, 0.19f, 0.21f);
    internal static readonly Color AccentColor = new(0.40f, 0.70f, 0.68f);
    internal static readonly Color AmberColor = new(0.82f, 0.61f, 0.31f);
    internal static readonly Color EdgeColor = new(0.32f, 0.36f, 0.39f);
    internal static readonly Color RecessColor = new(0.045f, 0.055f, 0.065f);

    internal static Vector2 WindowSize => new(
        Math.Max(1f, Math.Min(1180f, UI.screenWidth - 32f)),
        Math.Max(1f, Math.Min(860f, UI.screenHeight - 32f)));

    internal static float LabelHeight(string text, float width) => Math.Max(24f, Text.CalcHeight(text ?? string.Empty, Math.Max(1f, width)));
    internal static void DrawConsoleShell(Rect rect)
    {
        Widgets.DrawBoxSolid(rect, RecessColor);
        DrawMetalPanel(rect.ContractedBy(3f), new Color(0.12f, 0.14f, 0.16f), true);
        Widgets.DrawBoxSolid(new Rect(rect.x + 12f, rect.y + 13f, 3f, 27f), AccentColor);
        Widgets.DrawBoxSolid(new Rect(rect.x + 20f, rect.y + 53f, rect.width - 40f, 1f), EdgeColor);
        Widgets.DrawBoxSolid(new Rect(rect.x + 12f, rect.yMax - 15f, 52f, 3f), EdgeColor);
        Widgets.DrawBoxSolid(new Rect(rect.xMax - 64f, rect.yMax - 15f, 52f, 3f), EdgeColor);
    }
    internal static void DrawMetalPanel(Rect rect, Color fill, bool bolts = false)
    {
        Widgets.DrawBoxSolid(rect, RecessColor);
        Widgets.DrawBoxSolid(rect.ContractedBy(2f), fill);
        Widgets.DrawBoxSolid(new Rect(rect.x + 1f, rect.y + 1f, Math.Max(1f, rect.width - 2f), 1f), EdgeColor);
        Widgets.DrawBoxSolid(new Rect(rect.x + 1f, rect.y + 1f, 1f, Math.Max(1f, rect.height - 2f)), EdgeColor);
        if (!bolts) return;
        foreach (Vector2 p in stackalloc Vector2[] { new Vector2(rect.x + 5f, rect.y + 5f), new Vector2(rect.xMax - 9f, rect.y + 5f),
            new Vector2(rect.x + 5f, rect.yMax - 9f), new Vector2(rect.xMax - 9f, rect.yMax - 9f) })
        {
            Widgets.DrawBoxSolid(new Rect(p.x, p.y, 4f, 4f), EdgeColor);
            Widgets.DrawBoxSolid(new Rect(p.x + 1f, p.y + 2f, 2f, 1f), RecessColor);
        }
    }
    internal static void DrawSegments(Rect rect, int lit, Color color)
    {
        float width = (rect.width - 12f) / 5f;
        for (int i = 0; i < 5; i++) Widgets.DrawBoxSolid(new Rect(rect.x + i * (width + 3f), rect.y, width, rect.height), i < lit ? color : RecessColor);
    }
    internal static bool ControlButton(Rect rect, string label, bool enabled = true, bool active = false, bool operational = false)
    {
        enabled &= GUI.enabled;
        bool hover = enabled && Mouse.IsOver(rect);
        Color signal = operational ? AmberColor : AccentColor;
        DrawMetalPanel(rect, !enabled ? new Color(0.13f, 0.145f, 0.16f) : hover ? new Color(0.25f, 0.30f, 0.32f)
            : active ? new Color(0.21f, 0.28f, 0.29f) : CardColor);
        Widgets.DrawBoxSolid(new Rect(rect.x + 3f, rect.y + 4f, 3f, rect.height - 8f), enabled && (active || hover || operational) ? signal : EdgeColor);
        TextAnchor anchor = Text.Anchor; Color color = GUI.color;
        Text.Anchor = TextAnchor.MiddleCenter; GUI.color = enabled ? Color.white : new Color(0.53f, 0.57f, 0.60f);
        Widgets.Label(new Rect(rect.x + 10f, rect.y + 1f, Math.Max(1f, rect.width - 18f), rect.height - 2f), label);
        Text.Anchor = anchor; GUI.color = color;
        return enabled && Widgets.ButtonInvisible(rect);
    }

    internal static void DrawParagraph(float x, ref float y, float width, string text)
    {
        float height = LabelHeight(text, width);
        Widgets.Label(new Rect(x, y, width, height), text); y += height + 6f;
    }
    internal static bool ActionButton(Rect rect, string label, bool enabled, string tooltip)
    {
        bool pressed = ControlButton(rect, label, enabled, operational: true);
        if (!string.IsNullOrEmpty(tooltip)) TooltipHandler.TipRegion(rect, tooltip);
        return pressed;
    }

    internal static void DrawQueueCard(Rect row, bool first, float textWidth, float progress)
    {
        DrawMetalPanel(row, first ? new Color(0.18f, 0.24f, 0.25f) : PanelColor);
        Widgets.DrawBoxSolid(new Rect(row.x + 1f, row.y + 2f, 3f, row.height - 4f), first ? AmberColor : EdgeColor);
        Rect bar = new(row.x + 8f, row.yMax - 7f, textWidth, 3f);
        Widgets.DrawBoxSolid(bar, new Color(0.07f, 0.09f, 0.10f));
        Widgets.DrawBoxSolid(new Rect(bar.x, bar.y, bar.width * Mathf.Clamp01(progress), bar.height), AccentColor);
    }

    internal static bool DrawHeader(Rect rect, string title, int lit, Color signal, string status)
    {
        DrawConsoleShell(rect);
        Rect close = new(rect.xMax - 48f, rect.y + 12f, 32f, 28f);
        if (ControlButton(close, "×")) return true;
        TooltipHandler.TipRegion(close, L.T("MT_UIR_Close"));
        Text.Font = GameFont.Medium;
        Widgets.Label(new Rect(rect.x + 20f, rect.y + 16f, Math.Max(1f, rect.width - 160f), 34f), title);
        Text.Font = GameFont.Small;
        Rect indicator = new(rect.xMax - 118f, rect.y + 23f, 58f, 8f);
        DrawSegments(indicator, lit, signal);
        TooltipHandler.TipRegion(new Rect(indicator.x, rect.y + 12f, indicator.width, 32f), status);
        return false;
    }
}
