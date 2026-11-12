using System;
using Verse;
namespace ModularTurrets.Compat.QuantumShields;
// Compiled privately into each optional DLL; neither DLL needs the other.
internal static class QuantumDamage
{
    internal static bool Matches(DamageInfo damage)
        => damage.Def?.defName == "MT_QuantumDamage" && damage.Instigator is Building_ModularTurret
            && damage.Weapon?.defName is "MT_Gun_Quantum" or "MT_CE_Projectile_Quantum";
    internal static bool From(Def def, string package)
        => string.Equals(def?.modContentPack?.PackageIdPlayerFacing, package, StringComparison.OrdinalIgnoreCase);
}
