using System;
using System.Collections.Generic;
using Verse;

namespace ModularTurrets;

// Build descriptions on demand; no per-tick work or global tooltip patch.
internal static class TurretPartContext
{
    internal static bool UsesCE => CombatCompatibility.Provider != null;

    internal static Building_ModularTurret OwnerOf(Thing part)
    {
        IThingHolder holder = part?.ParentHolder;
        for (int depth = 0; holder != null && depth < 16; depth++, holder = holder.ParentHolder)
            if (holder is Building_ModularTurret turret) return turret;
        return null;
    }

    internal static string Header(Building_ModularTurret owner)
        => L.T("MT_ContextHeader", L.T(!CombatCompatibility.IsAvailable ? "MT_ContextUnavailable"
                : UsesCE ? "MT_ContextCE" : "MT_ContextCore"),
            owner?.HeadPart is { } head ? L.Label(head.def) : L.T("MT_ContextUnfitted"));

    internal static string Description(ThingDef part, Building_ModularTurret owner)
    {
        var ext = part.GetModExtension<PartExtension>();
        if (ext?.moduleKind == "FireControl") return L.T("MT_ContextFireControlIntro");
        if (ext?.isHead == true && ext.usesShells)
            return L.T(UsesCE ? "MT_ContextMortarCE" : "MT_ContextMortarCore");
        var specialist = part.GetModExtension<SpecialistModuleExtension>();
        if (specialist?.kind == "MortarSupport")
            return L.T(UsesCE ? "MT_ContextSupportCE" : "MT_ContextSupportCore");
        if (specialist?.kind == "MortarPayload") return L.T("MT_ContextPayload");
        return L.Description(part);
    }

    internal static string SpecialistEffects(Thing part, Building_ModularTurret owner)
    {
        var ext = part.def.GetModExtension<SpecialistModuleExtension>();
        if (ext?.kind is not ("MortarSupport" or "MortarPayload")) return Description(part.def, owner);
        var lines = new List<string> { Header(owner), Description(part.def, owner) };
        if (ext.kind == "MortarPayload")
        {
            lines.Add(L.T("MT_ContextPayloadDamage", SpecialistModuleExtension.Factor(ext.damageFactor, 1f)));
            lines.Add(L.T("MT_ContextPayloadRadius", SpecialistModuleExtension.Factor(ext.explosionRadiusFactor, 1f)));
        }
        else
        {
            lines.Add(L.T("MT_ContextSupportTiming", 1f - SpecialistModuleExtension.Factor(ext.warmupFactor, 1f),
                1f - SpecialistModuleExtension.Factor(ext.cooldownFactor, 1f)));
            lines.Add(L.T("MT_ContextSupportSupply", ext.autoReloadRadius));
            if (!UsesCE) lines.Add(L.T("MT_ContextRapidSpeed"));
        }
        return string.Join("\n", lines);
    }
}
