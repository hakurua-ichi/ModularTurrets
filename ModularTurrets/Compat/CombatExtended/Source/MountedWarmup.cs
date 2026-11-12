using System;
using System.Linq;
using CombatExtended;
using RimWorld;
using UnityEngine;
using Verse;

namespace ModularTurrets.Compat.CombatExtended;

internal static class MountedWarmup
{
    internal static void Reduce(Verb_ShootCE verb,MountedAimState state,float lastRotation,ref float recoil,ref float? stored)
    {
        if(verb.Caster is not Building_ModularTurret owner||!Controller.settings.FasterRepeatShots||verb.CompAmmo?.HasMagazine==false)return;
        var target=verb.CurrentTarget;if(!target.IsValid)return;
        Vector3 from=owner.MuzzleFor(verb.EquipmentSource),to=target.Thing?.TrueCenter()??target.Cell.ToVector3Shifted();
        if(target.Thing is Pawn pawn)to+=pawn.Drawer.leaner.LeanOffset*0.5f;
        Vector3 deltaPosition=to-from;
        float rotation=(-90f+Mathf.Rad2Deg*Mathf.Atan2(deltaPosition.z,deltaPosition.x))%360f;
        float delta=Mathf.Abs(rotation-lastRotation)+recoil;recoil=0f;
        float maximum=stored??(verb.CompFireModes?.CurrentAimMode==AimMode.SuppressFire?0.1f:state.Pending?0.5f:0.25f);
        float reduction=Mathf.Max(maximum,delta/45f);stored=reduction;
        if(reduction>=1f)return;
        if(state.Pending)state.Ticks=(int)(state.Ticks*reduction);
        else if(owner.CombatChannels.FirstOrDefault(c=>c.Verb==verb) is {} channel)channel.Warmup=(int)(channel.Warmup*reduction);
    }
}
