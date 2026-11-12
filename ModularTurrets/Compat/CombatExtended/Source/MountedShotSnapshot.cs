using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using CombatExtended;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace ModularTurrets.Compat.CombatExtended;

public sealed class MountedShotSnapshot : IExposable
{
    public float Damage,ArmorQuality=1f;
    public float ModularFactor=1f,ArmorFactor=1f;
    public float DamageQuality=1f;
    public float MortarDamageFactor=1f, MortarRadiusFactor=1f;
    public Faction Faction;
    public bool PreventFriendlyFire,Arrived,MortarShot;
    public void ExposeData()
    {
        Scribe_Values.Look(ref Damage,"damage");Scribe_Values.Look(ref ArmorQuality,"ceArmorQuality",1f);
        Scribe_Values.Look(ref ModularFactor,"modularFactor",1f);Scribe_Values.Look(ref ArmorFactor,"modularArmorFactor",1f);
        Scribe_Values.Look(ref DamageQuality,"ceDamageQuality",1f);
        Scribe_Values.Look(ref MortarDamageFactor,"mtMortarDamageFactor",1f);
        Scribe_Values.Look(ref MortarRadiusFactor,"mtMortarRadiusFactor",1f);
        Scribe_References.Look(ref Faction,"firingFaction");Scribe_Values.Look(ref PreventFriendlyFire,"preventFriendlyFire");
        Scribe_Values.Look(ref Arrived,"arrived");
        Scribe_Values.Look(ref MortarShot,"mtMortarShot",false);
    }
    internal MountedShotSnapshot Copy(bool arrived)=>new(){Damage=Damage,ArmorQuality=ArmorQuality,DamageQuality=DamageQuality,ModularFactor=ModularFactor,ArmorFactor=ArmorFactor,Faction=Faction,PreventFriendlyFire=PreventFriendlyFire,Arrived=arrived,
        MortarDamageFactor=MortarDamageFactor,MortarRadiusFactor=MortarRadiusFactor,MortarShot=MortarShot};
}
