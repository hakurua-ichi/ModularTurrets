using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.Linq;
using Verse;

namespace ModularTurrets;

/// <summary>Game translations by default; explicit mod language dictionaries are XML driven.</summary>
public static class L
{
    private static string loadedSelection;
    private static Dictionary<string, string> text;
    private static Dictionary<string, string> labels;
    private static string Selection => ModularTurretsMod.Instance?.Settings?.CurrentLanguage ?? "auto";
    public static IEnumerable<ModLanguageDef> Languages => DefDatabase<ModLanguageDef>.AllDefsListForReading.OrderBy(d => d.order).ThenBy(d => d.label);
    public static string ValueFor(ModLanguageDef language) => language.IsAutomatic ? "auto" : language.defName;
    public static string LanguageName(ModLanguageDef language) => language.IsAutomatic
        ? T("MT_LanguageAutomatic", LanguageDatabase.activeLanguage?.FriendlyNameNative ?? T("MT_LanguageGameDefault"))
        : Label(language);

    public static void Reload() { loadedSelection = null; text = null; labels = null; }

    public static string T(string key, params object[] args)
    {
        string template = null;
        if (Selection == "auto" && LanguageDatabase.activeLanguage != null)
            template = key.Translate().ToString();
        if (template == null || template == key)
        {
            EnsureLoaded();
            if (!text.TryGetValue(key, out template)) template = key;
        }
        if (args == null || args.Length == 0) return template;
        try { return string.Format(CultureInfo.CurrentCulture, template, args); }
        catch (FormatException)
        {
            Log.ErrorOnce("[Modular Turrets] Invalid translation format: " + key, GenText.StableStringHash(key));
            return template;
        }
    }

    public static string Label(Def definition)
    {
        if (definition == null) return string.Empty;
        if (Selection == "auto") return definition.LabelCap.ToString();
        EnsureLoaded();
        if (text.TryGetValue("MT_Resource_" + definition.defName, out string resource)) return resource;
        return TryGetDefText(definition, "label", out string label)
            ? label.CapitalizeFirst() : definition.LabelCap.ToString();
    }

    public static string Description(Def definition)
    {
        if (definition == null) return string.Empty;
        if (Selection == "auto") return definition.description ?? string.Empty;
        EnsureLoaded();
        return TryGetDefText(definition, "description", out string description)
            ? description : definition.description ?? string.Empty;
    }

    private static bool TryGetDefText(Def definition, string field, out string value)
    {
        for (Type type = definition.GetType(); type != null && typeof(Def).IsAssignableFrom(type); type = type.BaseType)
            if (labels.TryGetValue(type.FullName + "/" + definition.defName + "." + field, out value)
                || labels.TryGetValue(type.Name + "/" + definition.defName + "." + field, out value)) return true;
        value = null;
        return false;
    }

    private static void EnsureLoaded()
    {
        string selection = Selection;
        if (loadedSelection == selection && text != null) return;
        loadedSelection = selection;
        text = new Dictionary<string, string>(StringComparer.Ordinal);
        labels = new Dictionary<string, string>(StringComparer.Ordinal);
        string root = ModularTurretsMod.Instance?.Content?.RootDir;
        if (string.IsNullOrEmpty(root)) return;
        // Core selects these folders using LoadFolders conditions; optional
        // translations must not be read when their compatibility is inactive.
        string[] roots = (ModularTurretsMod.Instance.Content.foldersToLoadDescendingOrder?.AsEnumerable()
            ?? new[] { root }).Reverse().Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (roots.Length == 0) roots = new[] { root };
        foreach (string loadedRoot in roots) ReadLanguage(loadedRoot, "English");
        ModLanguageDef language = Languages.FirstOrDefault(d => d.defName == selection && !d.IsAutomatic);
        if (language != null && !string.Equals(language.folder, "English", StringComparison.OrdinalIgnoreCase))
            foreach (string loadedRoot in roots) ReadLanguage(loadedRoot, language.folder);
        if (selection != "auto") foreach (string loadedRoot in roots) ReadBaseLabels(loadedRoot);
    }

    private static void ReadLanguage(string modRoot, string folder)
    {
        string directory = LanguageDirectory(modRoot, folder);
        if (directory == null) return;
        ReadXmlFiles(Path.Combine(directory, "Keyed"), document =>
        {
            foreach (XElement entry in document.Root?.Elements() ?? Enumerable.Empty<XElement>())
                text[entry.Name.LocalName] = entry.Value.Replace("\\n", "\n");
        });
        string injected = Path.Combine(directory, "DefInjected");
        if (!Directory.Exists(injected)) return;
        foreach (string typeDirectory in Directory.EnumerateDirectories(injected).OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
        {
            if ((File.GetAttributes(typeDirectory) & FileAttributes.ReparsePoint) != 0) continue;
            string typeName = Path.GetFileName(typeDirectory);
            ReadXmlFiles(typeDirectory, document =>
            {
                foreach (XElement entry in document.Root?.Elements() ?? Enumerable.Empty<XElement>())
                    labels[typeName + "/" + entry.Name.LocalName] = entry.Value.Replace("\\n", "\n");
            });
        }
    }

    private static string LanguageDirectory(string modRoot, string folder)
    {
        if (string.IsNullOrWhiteSpace(folder) || Path.IsPathRooted(folder)) return null;
        try
        {
            string languagesRoot = Path.GetFullPath(Path.Combine(modRoot, "Languages"));
            if (Directory.Exists(languagesRoot) && (File.GetAttributes(languagesRoot) & FileAttributes.ReparsePoint) != 0) return null;
            string candidate = Path.GetFullPath(Path.Combine(languagesRoot, folder));
            if (!candidate.StartsWith(languagesRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) return null;
            // Each path segment must be a real directory inside this mod, not a link elsewhere.
            for (string current = candidate; current != languagesRoot; current = Path.GetDirectoryName(current))
                if (Directory.Exists(current) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) return null;
            return candidate;
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException) { return null; }
    }

    private static void ReadBaseLabels(string modRoot)
    {
        // Raw English Def text avoids changing global Defs when only this mod's UI language changes.
        ReadXmlFiles(Path.Combine(modRoot, "Defs"), document =>
        {
            foreach (XElement definition in document.Root?.Elements() ?? Enumerable.Empty<XElement>())
            {
                string name = definition.Element("defName")?.Value;
                if (name == null) continue;
                string typeName = definition.Name.LocalName.Split('.').Last();
                foreach (string field in new[] { "label", "description" })
                {
                    string value = definition.Element(field)?.Value;
                    string key = typeName + "/" + name + "." + field;
                    if (value != null && !labels.ContainsKey(key)) labels[key] = value.Replace("\\n", "\n");
                }
            }
        });
    }

    private static void ReadXmlFiles(string directory, Action<XDocument> read)
    {
        if (!Directory.Exists(directory) || (File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) return;
        foreach (string file in Directory.EnumerateFiles(directory, "*.xml", SearchOption.TopDirectoryOnly).OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
        {
            if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0) continue;
            try
            {
                using XmlReader reader = XmlReader.Create(file, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
                read(XDocument.Load(reader));
            }
            catch (Exception exception) when (exception is XmlException or IOException or UnauthorizedAccessException)
            {
                Log.Warning("[Modular Turrets] Could not read language XML: " + Path.GetFileName(file) + " (" + exception.GetType().Name + ")");
            }
        }
    }
}
