using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace ModularTurrets;

public sealed partial class Building_ModularTurret
{
    private float activeShotFactor = 1f;
    private ModuleModifiers cachedModifiers, calculatingModifiers;
    private sealed class EffectGroup
    {
        public ModuleEffectDef Definition;
        public Thing[] Parts;
    }
    private EffectGroup[] cachedEffectGroups;
    public void InvalidateEffects() { cachedModifiers = null; cachedEffectGroups = null; }
    private EffectGroup[] EffectGroups()
    {
        if (cachedEffectGroups != null) return cachedEffectGroups;
        var indices = new Dictionary<ModuleEffectDef, int>();
        var definitions = new List<ModuleEffectDef>();
        var parts = new List<List<Thing>>();
        foreach (var mount in fitted)
        {
            var effects = mount.Part.def.GetModExtension<PartExtension>()?.effects;
            if (effects == null) continue;
            foreach (var definition in effects)
            {
                if (definition == null) continue;
                if (!indices.TryGetValue(definition, out int index))
                {
                    index = definitions.Count;
                    indices.Add(definition, index); definitions.Add(definition); parts.Add(new());
                }
                parts[index].Add(mount.Part);
            }
        }
        var groups = new EffectGroup[definitions.Count];
        for (int i = 0; i < groups.Length; i++) groups[i] = new() { Definition = definitions[i], Parts = parts[i].ToArray() };
        return cachedEffectGroups = groups;
    }
    private IEnumerable<ModuleEffectContext> ActiveEffects()
    {
        // Cache configuration, not winners: tiers and paragon eligibility may
        // change between calls, or a worker may affect the following group.
        foreach (var group in EffectGroups())
        {
            Thing strongest = null; float highest = 0f;
            foreach (Thing part in group.Parts)
            {
                float power = ParagonUtility.Power(part);
                if (strongest != null && power.CompareTo(highest) <= 0) continue;
                strongest = part; highest = power;
            }
            // Workers receive a fresh context, as before; none is shared across shots.
            yield return new ModuleEffectContext { Turret = this, Part = strongest, Definition = group.Definition };
        }
    }
    private ModuleModifiers ExtraModifiers()
    {
        if (cachedModifiers != null) return cachedModifiers;
        if (calculatingModifiers != null) return calculatingModifiers;
        var m = new ModuleModifiers(); calculatingModifiers = m;
        try
        {
            foreach (var context in ActiveEffects()) context.Definition.Worker.ModifyStats(context, m);
            return cachedModifiers = m;
        }
        finally { calculatingModifiers = null; }
    }
    public List<ModuleShot> BuildShots(Thing equipment, LocalTargetInfo target)
    {
        var shots = new List<ModuleShot> { new() { Target = target } };
        bool auxiliary = true;
        foreach (var channel in channels)
            if (channel.Gun == equipment) { auxiliary = channel.Slot != "head"; break; }
        foreach (var context in ActiveEffects())
            if (!auxiliary || context.Definition.affectsAuxiliary) context.Definition.Worker.TransformShots(context, equipment, shots);
        if (shots.Count > 9) shots.RemoveRange(9, shots.Count - 9);
        return shots;
    }
    public bool WithShotFactor(float factor, Func<bool> action)
    {
        float previous = activeShotFactor; activeShotFactor *= factor;
        try { return action(); } finally { activeShotFactor = previous; }
    }
}
