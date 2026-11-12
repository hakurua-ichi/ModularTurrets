using System;
using System.Runtime.CompilerServices;
using RimWorld;
using UnityEngine;
using Verse;

namespace ModularTurrets.Batteries;

public sealed partial class Building_ModularBattery
{
    // Building_Battery.DrawAt unconditionally draws Core's yellow bar and
    // exposes no hook for replacing it. Preserve its burning-wick indicator.
    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "ticksToExplode")]
    private static extern ref int WickTicks(Building_Battery battery);

    protected override void DrawAt(Vector3 drawLoc, bool flip = false)
    {
        bool drawParent = true;
        foreach (ThingComp comp in AllComps)
            if (comp.DontDrawParent()) { drawParent = false; break; }
        if (drawParent)
        {
            Rot4 rotation = flip ? Rotation.Opposite : Rotation;
            if (!Spawned || def.drawerType == DrawerType.RealtimeOnly)
                Graphic.Draw(drawLoc, rotation, this);
            SilhouetteUtility.DrawGraphicSilhouette(this, drawLoc);
            if (Battery is { } battery)
                BatteryChargeGraphic.Draw(drawLoc, rotation, battery.ChargeFraction);
        }
        Comps_DrawAt(drawLoc, flip);
        Comps_PostDraw();
        if (Spawned && WickTicks(this) > 0)
            Map.overlayDrawer.DrawOverlay(this, OverlayTypes.BurningWick);
    }
}
