using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace ModularTurrets;

public sealed partial class Building_ModularTurret
{
    private int lastFlameDamageTick = -1, lastFlameVisualTick = -1;
    private readonly HashSet<int> flameTargetsThisTick = new();

    internal bool ClaimFlameTarget(Thing target)
    {
        int now = Find.TickManager.TicksGame;
        if (lastFlameDamageTick != now) { lastFlameDamageTick = now; flameTargetsThisTick.Clear(); }
        return flameTargetsThisTick.Add(target.thingIDNumber);
    }

    internal bool ClaimFlameVisual()
    {
        int now = Find.TickManager.TicksGame;
        if (lastFlameVisualTick == now) return false;
        lastFlameVisualTick = now;
        return true;
    }
}
