using System;
using System.Collections.Generic;
using System.Text;
using ModularTurrets.Batteries;
using RimWorld;
using Verse;

namespace ModularTurrets;

public sealed partial class CompTurretPart
{
    public override float GetStatOffset(StatDef stat)
        => base.GetStatOffset(stat) + ModularMarketValue.StatOffset(parent, stat);

    public override void GetStatsExplanation(StatDef stat, StringBuilder text, string whitespace = "")
    {
        base.GetStatsExplanation(stat, text, whitespace);
        ModularMarketValue.Explain(parent, stat, text, whitespace);
    }
}
