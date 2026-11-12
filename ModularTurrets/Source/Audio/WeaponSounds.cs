using System.Linq;
using Verse;

namespace ModularTurrets;

public static class WeaponSounds
{
    public static void Apply(ThingDef weapon, VerbProperties template, VerbProperties instance)
    {
        instance.soundCast = template.soundCast;
        instance.soundCastTail = template.soundCastTail;
        if (ModularTurretsMod.Instance?.Settings?.TrialWeaponSounds != true) return;
        var profile = DefDatabase<TurretSoundProfileDef>.AllDefsListForReading.FirstOrDefault(p => p.weapon == weapon);
        if (profile?.cast == null) return;
        instance.soundCast = profile.cast;
        instance.soundCastTail = profile.tail;
    }

    public static void RefreshAll()
    {
        if (Find.Maps == null) return;
        foreach (var map in Find.Maps)
            foreach (var turret in map.listerThings.AllThings.OfType<Building_ModularTurret>())
                turret.RefreshWeaponSounds();
    }
}
