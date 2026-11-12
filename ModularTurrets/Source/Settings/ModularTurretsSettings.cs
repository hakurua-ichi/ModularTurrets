using Verse;

namespace ModularTurrets;

public sealed class ModularTurretsSettings : ModSettings
{
    public string CurrentLanguage = "auto";
    // Keep the saved sound preference after retiring its experimental UI.
    public bool TrialWeaponSounds = true;
    public bool SpriteInstancing = true;
    public bool TeslaVisualInstancing = true;
    public bool TeslaParallelGeometry = true;

    public override void ExposeData()
    {
        Scribe_Values.Look(ref CurrentLanguage, "currentLanguage", "auto");
        Scribe_Values.Look(ref TrialWeaponSounds, "trialWeaponSounds", true);
        Scribe_Values.Look(ref SpriteInstancing, "spriteInstancing", true);
        Scribe_Values.Look(ref TeslaVisualInstancing, "teslaVisualInstancing", true);
        Scribe_Values.Look(ref TeslaParallelGeometry, "teslaParallelGeometry", true);
        CurrentLanguage ??= "auto";
    }
}
