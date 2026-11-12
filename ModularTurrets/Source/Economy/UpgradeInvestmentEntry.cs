using System;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace ModularTurrets;

public sealed class UpgradeInvestmentEntry : IExposable
{
    public ThingDef Resource;
    public long Count;
    public void ExposeData()
    {
        Scribe_Defs.Look(ref Resource, "resource");
        Scribe_Values.Look(ref Count, "count", 0L);
    }
}
