using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using CombatExtended;
using CombatExtended.Compatibility;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace ModularTurrets.Compat.CombatExtended;
public sealed class CompMountedCE : ThingComp
{
    public bool InitialMagazineTransferred;
    public int LegacyShellsTransferred;
    public bool Reloading;
    public Thing ReloadGun;
    public override void PostExposeData()
    {
        base.PostExposeData();Scribe_Values.Look(ref InitialMagazineTransferred,"mtCEInitialMagazineTransferred");
        Scribe_Values.Look(ref LegacyShellsTransferred,"mtCELegacyShellsTransferred");
        Scribe_Values.Look(ref Reloading,"mtCEReloading");Scribe_References.Look(ref ReloadGun,"mtCEReloadGun");
    }
}
