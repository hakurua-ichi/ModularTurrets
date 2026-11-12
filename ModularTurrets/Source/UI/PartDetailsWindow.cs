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

        private sealed class PartDetailsWindow : Window
        {
            private readonly AssemblyWindow owner;
            private readonly string slot;
            private readonly Thing part;
            private Vector2 scroll;
            protected override float Margin => 0f;
            public override Vector2 InitialSize => new(Math.Max(1f, Math.Min(700f, UI.screenWidth - 48f)),
                Math.Max(1f, Math.Min(640f, UI.screenHeight - 48f)));
            public PartDetailsWindow(AssemblyWindow owner, string slot, Thing part)
            {
                this.owner = owner; this.slot = slot; this.part = part;
                doWindowBackground = false; doCloseX = false; draggable = true; absorbInputAroundWindow = true;
            }
            public override void DoWindowContents(Rect rect)
            {
                if (!owner.IsOpen || owner.turret.Destroyed || !owner.turret.Spawned || owner.turret.PartAt(slot) != part)
                { Close(); return; }
                var font = Text.Font; var anchor = Text.Anchor; var color = GUI.color; bool wrap = Text.WordWrap;
                try
                {
                    Text.Font = GameFont.Small; Text.Anchor = TextAnchor.UpperLeft; Text.WordWrap = true; GUI.color = Color.white;
                    DrawMetalPanel(rect, PanelColor, true);
                    Rect icon = new(18f, 16f, 68f, 68f);
                    SlotPartIcons.Draw(icon, part, slot); DrawParagonBadge(icon, part);
                    float titleY = 18f;
                    DrawParagraph(100f, ref titleY, rect.width - 120f, L.T("MT_PartDetailsTitle", L.Label(part.def)));
                    float top = Math.Max(98f, titleY + 12f);
                    Rect viewport = new(20f, top, rect.width - 40f, Math.Max(1f, rect.height - top - 68f));
                    float width = Math.Max(1f, viewport.width - 20f);
                    string text = owner.PartDescription(slot, part);
                    Widgets.BeginScrollView(viewport, ref scroll, new Rect(0f, 0f, width, LabelHeight(text, width) + 12f));
                    try { float y = 0f; DrawParagraph(0f, ref y, width, text); }
                    finally { Widgets.EndScrollView(); }
                    if (ControlButton(new Rect(20f, rect.height - 52f, rect.width - 40f, 34f), L.T("MT_UIR_BackToTurret"))) Close();
                }
                finally { Text.Font = font; Text.Anchor = anchor; Text.WordWrap = wrap; GUI.color = color; }
            }
        }
    }
}
