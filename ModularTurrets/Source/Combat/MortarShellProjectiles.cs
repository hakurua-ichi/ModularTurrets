using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using RimWorld;
using Verse;

namespace ModularTurrets;

[StaticConstructorOnStartup]
public static class MortarShellProjectiles
{
    private static readonly Dictionary<ThingDef, ThingDef> projectiles = new();
    private static readonly Dictionary<ThingDef, ThingDef> rapidProjectiles = new(ReferenceEqualityComparer.Instance);
    private static readonly HashSet<ThingDef> registeredProjectiles = new(ReferenceEqualityComparer.Instance);
    private static readonly List<ThingDef> shells = new();
    private static readonly Dictionary<string, string> standardProjectiles = new(StringComparer.Ordinal)
    {
        ["Shell_HighExplosive"] = "Bullet_Shell_HighExplosive",
        ["Shell_Incendiary"] = "Bullet_Shell_Incendiary",
        ["Shell_EMP"] = "Bullet_Shell_EMP",
        ["Shell_Smoke"] = "Bullet_Shell_Smoke",
        ["Shell_Firefoam"] = "Bullet_Shell_Firefoam",
        ["Shell_AntigrainWarhead"] = "Bullet_Shell_AntigrainWarhead"
    };
    public static IReadOnlyList<ThingDef> Shells => shells;

    static MortarShellProjectiles()
    {
        var clone = typeof(object).GetMethod("MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic);
        var hashType = typeof(ShortHashGiver);
        var giveHash = hashType.GetMethod("GiveShortHash", BindingFlags.Static | BindingFlags.NonPublic,
            null, new[] { typeof(Def), typeof(Type), typeof(HashSet<ushort>) }, null);
        var taken = hashType.GetField("takenHashesPerDeftype", BindingFlags.Static | BindingFlags.NonPublic)
            ?.GetValue(null) as Dictionary<Type, HashSet<ushort>>;
        HashSet<ushort> hashes = null;
        if (giveHash == null || taken == null)
            Log.ErrorOnce("[Modular Turrets] Special mortar shells are disabled because Core's short-hash registration API changed. Existing high-explosive mortar ammunition remains available.",
                GenText.StableStringHash("MTMortarShellHashAPI"));
        else if (!taken.TryGetValue(typeof(ThingDef), out hashes))
            taken[typeof(ThingDef)] = hashes = new HashSet<ushort>(DefDatabase<ThingDef>.AllDefsListForReading.Select(d => d.shortHash));
        var generated = new Dictionary<ThingDef, ThingDef>();
        var rapidAttempted = new HashSet<ThingDef>(ReferenceEqualityComparer.Instance);
        var category = DefDatabase<ThingCategoryDef>.GetNamedSilentFail("MortarShells");
        foreach (var shell in DefDatabase<ThingDef>.AllDefsListForReading
            .Where(d => d.projectileWhenLoaded != null || standardProjectiles.ContainsKey(d.defName))
            .OrderBy(d => d.defName, StringComparer.Ordinal).ToArray())
        {
            bool standard = standardProjectiles.TryGetValue(shell.defName, out string originalName);
            // CE replaces the shell resource Defs and removes this field. Keep
            // the stable Core flight Defs registered for saves crossing to CE.
            ThingDef original = shell.projectileWhenLoaded
                ?? (standard ? DefDatabase<ThingDef>.GetNamedSilentFail(originalName) : null);
            // Replacing an arbitrary subclass would erase its custom Impact
            // behavior. Core's six ordinary shell types share this exact class.
            if ((!standard && (category == null || !shell.IsWithinCategory(category))) || original?.projectile == null
                || original.thingClass != typeof(Projectile_Explosive)) continue;
            ThingDef projectile;
            if (shell.defName == "Shell_HighExplosive")
                projectile = DefDatabase<ThingDef>.GetNamed("MT_Projectile_DualMortar");
            else
            {
                if (!generated.TryGetValue(original, out projectile))
                {
                    if (hashes == null) continue;
                    string name = "MT_MortarShell_" + original.defName;
                    // Multiple shell resources may share a projectile. Reuse only
                    // the wrapper made for that same original, never a foreign Def
                    // occupying its name; Core otherwise renames duplicates randomly.
                    if (DefDatabase<ThingDef>.GetNamedSilentFail(name) != null)
                    {
                        Log.ErrorOnce("[Modular Turrets] Mortar projectile name is already occupied: " + name
                            + ". Shells using that projectile are disabled to preserve the existing Def.",
                            GenText.StableStringHash("MTMortarShellName:" + name));
                        continue;
                    }
                    // Properties/graphics stay read-only shared data. In-flight
                    // shells belong to their firing map, not a departing deck.
                    projectile = (ThingDef)clone.Invoke(original, null);
                    projectile.defName = name;
                    projectile.thingClass = typeof(Projectile_ModularExplosive);
                    projectile.bringAlongOnGravship = false;
                    projectile.shortHash = 0;
                    // AddImpliedDef resolves the new Def identity and registers it,
                    // but does not assign short hashes after initial Def loading.
                    giveHash.Invoke(null, new object[] { projectile, typeof(ThingDef), hashes });
                    DefGenerator.AddImpliedDef(projectile);
                    generated.Add(original, projectile);
                }
            }
            projectiles[shell] = projectile;
            registeredProjectiles.Add(projectile);
            if (hashes != null && rapidAttempted.Add(projectile))
            {
                // Thing's ID parser reads all trailing digits as the entity ID.
                string name = projectile.defName + "_Rapid125Pct";
                float speed = projectile.projectile.speed * 1.25f;
                if (DefDatabase<ThingDef>.GetNamedSilentFail(name) != null || !float.IsFinite(speed) || speed <= 0f)
                    Log.ErrorOnce("[Modular Turrets] Rapid mortar projectile is unavailable: " + name
                        + ". Ordinary projectile flight is retained without changing the existing Def.",
                        GenText.StableStringHash("MTMortarRapid:" + name));
                else
                {
                    // A stable per-speed Def is the shot's snapshot. Keep Core's
                    // timing, position, arc and interception calculations together.
                    var rapid = (ThingDef)clone.Invoke(projectile, null);
                    rapid.defName = name;
                    rapid.projectile = (ProjectileProperties)clone.Invoke(projectile.projectile, null);
                    rapid.projectile.speed = speed;
                    rapid.bringAlongOnGravship = false;
                    rapid.shortHash = 0;
                    giveHash.Invoke(null, new object[] { rapid, typeof(ThingDef), hashes });
                    DefGenerator.AddImpliedDef(rapid);
                    rapidProjectiles.Add(projectile, rapid);
                    registeredProjectiles.Add(rapid);
                }
            }
            if (shell.projectileWhenLoaded != null) shells.Add(shell);
        }
    }

    public static bool Supports(ThingDef shell) => shell != null && projectiles.ContainsKey(shell);
    internal static bool IsShellResource(ThingDef shell)
        => shell != null && (shell.projectileWhenLoaded != null || standardProjectiles.ContainsKey(shell.defName));
    public static ThingDef ProjectileFor(ThingDef shell)
        => shell != null && projectiles.TryGetValue(shell, out var projectile) ? projectile : null;
    internal static ThingDef ProjectileFor(ThingDef shell, bool rapid)
    {
        ThingDef normal = ProjectileFor(shell);
        return rapid && normal != null && rapidProjectiles.TryGetValue(normal, out var accelerated) ? accelerated : normal;
    }
    internal static bool IsMortarProjectile(ThingDef projectile)
        => projectile != null && registeredProjectiles.Contains(projectile);
}
