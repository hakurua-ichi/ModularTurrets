using System;
using System.Linq;
using Verse;

namespace ModularTurrets;

public sealed partial class Building_ModularTurret
{
    // Shot-local only. Projectile Launch copies this into its existing saved
    // armor bonus; FireChannel owns the persistent once-per-burst readiness.
    private float paragonOpeningArmorBonus;
    public float ParagonOpeningArmorBonus => paragonOpeningArmorBonus;

    public bool WithParagonOpeningShot(Thing equipment, Func<bool> action)
    {
        var channel = channels.FirstOrDefault(c => c.Slot == "head" && c.Gun == equipment);
        float previous = paragonOpeningArmorBonus;
        paragonOpeningArmorBonus = 0f;
        if (channel?.OpeningShotReady == true && ActiveParagon("Aiming") is { } aiming)
        {
            float bonus = ParagonUtility.Settings(aiming.def).openingArmorBonus;
            if (float.IsFinite(bonus)) paragonOpeningArmorBonus = Math.Clamp(bonus, 0f, 0.25f);
        }
        try
        {
            bool fired = action();
            // A miss is still a fired opening shot. Failed casts retain readiness,
            // while adding an aiming module mid-burst must not grant another one.
            if (fired)
            {
                NoteShieldCombatActivity(Find.TickManager.TicksGame);
                if (channel != null) channel.OpeningShotReady = false;
            }
            return fired;
        }
        finally { paragonOpeningArmorBonus = previous; }
    }
}
