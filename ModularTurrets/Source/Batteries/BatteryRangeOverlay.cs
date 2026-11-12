using UnityEngine;
using Verse;

namespace ModularTurrets.Batteries;

public sealed partial class Building_ModularBattery
{
    private LongRangeOverlay longWirelessOverlay;

    public override void DrawExtraSelectionOverlays()
    {
        base.DrawExtraSelectionOverlays();
        if (!Spawned) return;
        float radius = Battery?.WirelessRadius ?? 0f;
        if (radius <= 0f) return;
        if (radius <= GenRadial.MaxRadialPatternRadius) GenDraw.DrawRadiusRing(Position, radius, Color.white);
        else (longWirelessOverlay ??= new LongRangeOverlay()).Draw(Map, Position, radius);
    }
}
