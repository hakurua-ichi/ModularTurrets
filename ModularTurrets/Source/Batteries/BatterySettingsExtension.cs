using System;
using System.Collections.Generic;
using Verse;

namespace ModularTurrets.Batteries;

public sealed class BatterySettingsExtension : DefModExtension
{
    public float capacityPerUpgrade = 1200f;
    public int upgradeSteel = 80;
    public int upgradeComponents = 2;
    public float upgradeWork = 600f;
    public float installWork = 100f;
    public float removeWork = 100f;
}
