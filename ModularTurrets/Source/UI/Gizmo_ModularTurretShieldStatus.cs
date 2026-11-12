using System;
using UnityEngine;
using Verse;

namespace ModularTurrets;

// Same selection and Gizmo layout convention as Core's Gizmo_EnergyShieldStatus.
[StaticConstructorOnStartup]
public sealed class Gizmo_ModularTurretShieldStatus : Gizmo
{
    private readonly Building_ModularTurret turret;
    private static readonly Texture2D FullBar = SolidColorMaterials.NewSolidColorTexture(new Color(0.18f, 0.36f, 0.43f));
    private static readonly Texture2D EmptyBar = SolidColorMaterials.NewSolidColorTexture(new Color(0.08f, 0.10f, 0.12f));

    public Gizmo_ModularTurretShieldStatus(Building_ModularTurret turret)
    {
        this.turret = turret;
        Order = -100f;
    }

    public override float GetWidth(float maxWidth) => 180f;

    public override GizmoResult GizmoOnGUI(Vector2 topLeft, float maxWidth, GizmoRenderParms parms)
    {
        Rect rect = new(topLeft.x, topLeft.y, GetWidth(maxWidth), 75f);
        var oldFont = Text.Font; var oldAnchor = Text.Anchor; var oldColor = GUI.color;
        bool oldWordWrap = Text.WordWrap;
        try
        {
            GUI.color = Color.white; Text.Anchor = TextAnchor.UpperLeft; Text.WordWrap = false;
            Widgets.DrawWindowBackground(rect);
            Rect inner = rect.ContractedBy(6f);
            Text.Font = GameFont.Tiny;
            Widgets.Label(new Rect(inner.x, inner.y - 1f, inner.width, 16f), L.T("MT_ShieldStatusTitle"));

            float maximum = turret.ShieldMaximum, energy = turret.CurrentShieldEnergy;
            Rect bar = new(inner.x, inner.y + 15f, inner.width, 22f);
            Widgets.FillableBar(bar, Mathf.Clamp01(energy / Math.Max(1f, maximum)), FullBar, EmptyBar, doBorder: true);
            Text.Font = GameFont.Small; Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(bar, L.T("MT_ShieldStatusEnergy", energy, maximum));

            int delay = turret.ShieldRechargeDelayTicks;
            string status = !turret.Powered ? L.T("MT_ShieldStatusNoPower")
                : energy >= maximum ? L.T("MT_ShieldStatusFull")
                : delay > 0 ? string.Empty
                : L.T("MT_ShieldStatusRecharging");
            Text.Font = GameFont.Tiny; Text.Anchor = TextAnchor.UpperLeft;
            Widgets.Label(new Rect(inner.x, inner.y + 38f, inner.width, 15f), status);

            TooltipHandler.TipRegion(rect, L.T("MT_ShieldStatusTip"));
        }
        finally
        {
            Text.Font = oldFont; Text.Anchor = oldAnchor; Text.WordWrap = oldWordWrap; GUI.color = oldColor;
        }
        return new GizmoResult(GizmoState.Clear);
    }
}
