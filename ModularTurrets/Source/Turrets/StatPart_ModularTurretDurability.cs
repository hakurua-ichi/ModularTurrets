using System;
using RimWorld;
using Verse;

namespace ModularTurrets;

public sealed class StatPart_ModularTurretDurability : StatPart
{
    public int headHitPoints = 200;
    public int moduleHitPoints = 100;
    public int moduleTierHitPoints = 50;

    public override void TransformValue(StatRequest req, ref float val)
    {
        if (req.Thing is Building_ModularTurret turret)
        {
            val += turret.InstalledDurabilityBonus(headHitPoints, moduleHitPoints, moduleTierHitPoints);
            if (turret.IsWrecked) val += turret.WreckHitPointsBonus;
        }
    }

    public override string ExplanationPart(StatRequest req)
        => req.Thing is Building_ModularTurret turret
            ? L.T("MT_DurabilityInstalled", turret.InstalledDurabilityBonus(headHitPoints, moduleHitPoints, moduleTierHitPoints))
                + (turret.IsWrecked ? "\n" + L.T("MT_DurabilityWreck", turret.WreckHitPointsBonus) : "")
            : null;
}
