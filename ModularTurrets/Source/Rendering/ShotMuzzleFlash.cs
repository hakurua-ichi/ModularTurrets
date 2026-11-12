using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using RimWorld;
using UnityEngine;
using Verse;

namespace ModularTurrets;

internal static class ShotMuzzleFlash
{
    private sealed class State
    {
        public VerbProperties Suppressed;
        public float Scale;
    }
    private static readonly ConditionalWeakTable<Verb, State> States = new();
    private static readonly MethodInfo Clone = typeof(object).GetMethod("MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic);

    public static float SuppressVanilla(Verb verb)
    {
        if (verb?.verbProps == null) return 0f;
        State state = States.GetValue(verb, static _ => new State());
        if (!ReferenceEquals(state.Suppressed, verb.verbProps))
        {
            state.Scale = verb.verbProps.muzzleFlashScale;
            // Core emits ShotFlash after TryCastShot returns, from caster.Position.
            // Keep it suppressed on a private copy until a rebuild supplies new props.
            state.Suppressed = (VerbProperties)Clone.Invoke(verb.verbProps, null);
            state.Suppressed.muzzleFlashScale = 0f;
            verb.verbProps = state.Suppressed;
        }
        return state.Scale;
    }
    public static void Emit(Building_ModularTurret turret, Vector3 point, float scale, FleckDef flash)
    {
        if (turret.Spawned && scale > 0.01f)
            FleckMaker.Static(point, turret.Map, flash ?? FleckDefOf.ShotFlash, scale);
    }
}
