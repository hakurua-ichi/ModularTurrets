using System.Linq;
using Verse;

namespace ModularTurrets;

public sealed partial class Building_ModularTurret
{
    public void RefreshWeaponSounds()
    {
        foreach (var channel in channels)
        {
            if (channel.Gun?.TryGetComp<CompEquippable>() is not { } equipment) continue;
            foreach (var verb in equipment.AllVerbs)
            {
                var template = channel.Gun.def.Verbs.FirstOrDefault(v => v.verbClass == verb.GetType());
                if (template != null) WeaponSounds.Apply(channel.Gun.def, template, verb.verbProps);
            }
        }
    }
}
