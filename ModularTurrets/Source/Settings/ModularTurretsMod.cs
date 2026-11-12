using System;
using System.Linq;
using UnityEngine;
using Verse;

namespace ModularTurrets;

public sealed class ModularTurretsMod : Mod
{
    public static ModularTurretsMod Instance;
    public readonly ModularTurretsSettings Settings;
    private Vector2 settingsScroll;
    private float settingsHeight;

    public ModularTurretsMod(ModContentPack content) : base(content)
    {
        Instance = this;
        Settings = GetSettings<ModularTurretsSettings>();
    }

    public override string SettingsCategory() => L.T("MT_SettingsCategory");

    public override void DoSettingsWindowContents(Rect inRect)
    {
        var font = Text.Font;
        var anchor = Text.Anchor;
        var color = GUI.color;
        bool wrap = Text.WordWrap;
        try
        {
            Text.Anchor = TextAnchor.UpperLeft;
            Text.WordWrap = true;
            GUI.color = Color.white;
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(inRect.x, inRect.y, inRect.width, 36f), L.T("MT_SettingsTitle"));
            Text.Font = GameFont.Small;
            Rect viewport = new(inRect.x, inRect.y + 48f, inRect.width, Math.Max(1f, inRect.height - 48f));
            float width = Math.Max(1f, viewport.width - 20f);
            Widgets.BeginScrollView(viewport, ref settingsScroll, new Rect(0f, 0f, width, Math.Max(viewport.height, settingsHeight)));
            try
            {
                float y = 0f;
                DrawLanguage(width, ref y);
                y += 24f;
                Text.Font = GameFont.Medium;
                DrawParagraph(L.T("MT_SettingsPerformanceTitle"), width, ref y);
                Text.Font = GameFont.Small;
                DrawParagraph(L.T("MT_SettingsPerformanceHint"), width, ref y);
                y += 12f;

                bool changed = false;
                if (DrawToggle("MT_RenderInstancingLabel", "MT_RenderInstancingHint", ref Settings.SpriteInstancing, width, ref y))
                {
                    if (Settings.SpriteInstancing) TurretRenderBatch.RequestRetry();
                    changed = true;
                }
                changed |= DrawToggle("MT_TeslaInstancingLabel", "MT_TeslaInstancingHint", ref Settings.TeslaVisualInstancing, width, ref y);
                if (DrawToggle("MT_TeslaParallelLabel", "MT_TeslaParallelHint", ref Settings.TeslaParallelGeometry, width, ref y))
                {
                    if (Settings.TeslaParallelGeometry) TurretGeometryPreDraw.RequestRetry();
                    changed = true;
                }
                settingsHeight = y;
                if (changed) WriteSettings();
            }
            finally { Widgets.EndScrollView(); }
        }
        finally
        {
            Text.Font = font;
            Text.Anchor = anchor;
            Text.WordWrap = wrap;
            GUI.color = color;
        }
        base.DoSettingsWindowContents(inRect);
    }

    private void DrawLanguage(float width, ref float y)
    {
        DrawParagraph(L.T("MT_SettingsLanguage"), width, ref y);
        var chosen = L.Languages.FirstOrDefault(d => L.ValueFor(d) == Settings.CurrentLanguage);
        string caption = chosen == null ? L.T("MT_LanguageUnavailable") : L.LanguageName(chosen);
        if (Widgets.ButtonText(new Rect(0f, y, Math.Min(420f, width), 34f), caption))
        {
            var options = L.Languages.Select(language => new FloatMenuOption(L.LanguageName(language), () =>
            {
                Settings.CurrentLanguage = L.ValueFor(language);
                L.Reload();
                WriteSettings();
            })).ToList();
            Find.WindowStack.Add(new FloatMenu(options));
        }
        y += 44f;
        DrawParagraph(L.T("MT_SettingsLanguageHint"), width, ref y);
    }

    private static bool DrawToggle(string labelKey, string hintKey, ref bool value, float width, ref float y)
    {
        string label = L.T(labelKey), hint = L.T(hintKey);
        float height = Math.Max(30f, Text.CalcHeight(label, Math.Max(1f, width - 32f)));
        Rect row = new(0f, y, width, height);
        bool before = value;
        Widgets.CheckboxLabeled(row, label, ref value);
        TooltipHandler.TipRegion(row, hint);
        y += height + 4f;
        DrawParagraph(hint, width, ref y);
        y += 18f;
        return before != value;
    }

    private static void DrawParagraph(string text, float width, ref float y)
    {
        float height = Text.CalcHeight(text, width);
        Widgets.Label(new Rect(0f, y, width, height), text);
        y += height + 6f;
    }
}
