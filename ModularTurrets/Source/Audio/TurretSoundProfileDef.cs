using System.Linq;
using Verse;

namespace ModularTurrets;

// Alternate sound references only; the original weapon Def remains the rollback source.
public sealed class TurretSoundProfileDef : Def
{
    public ThingDef weapon;
    public SoundDef cast;
    public SoundDef tail;
}
