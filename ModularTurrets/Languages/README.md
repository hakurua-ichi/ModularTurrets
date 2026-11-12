If you are translating this mod with AI, use the prompt below.

```text
Translate Modular Turrets into <target language>.
Use the native language name <native name>, the RimWorld language folder
<folder>, and a unique ModLanguageDef name <MT_Language...>.
Ask for these values if they have not been supplied.

1. Locate the mod root by Defs/Languages.xml and inspect Languages/README.md.
   If the source checkout is available, also inspect Source/Localization/L.cs.
   Use the current Languages/English XML as the translation source.
   Korean may provide context for an intentional joke or ambiguous wording.
2. Inventory English/Keyed and English/DefInjected by Def class. Also inspect
   each Compat/*/Languages/English tree that actually exists. Do not merge
   optional compatibility text into Core or invent missing compatibility code.
3. Create the target language trees, keeping the original XML filenames,
   keys, DefName.field paths and class-directory names. Put XML files directly
   in Keyed or DefInjected/<Def class>, without extra nested folders.
4. Translate only visible text. Preserve placeholders and format specifiers
   such as {0}, {1:F1} and {0:P0}, rich-text markup, XML escapes, numbers,
   units, probabilities and gameplay restrictions. Use literal XML &#10;
   for line breaks and &#10;&#10; for paragraphs. Do not write &amp;#10;.
5. Keep authored jokes in their existing descriptions, with comparable tone.
   Do not add new jokes, exaggerate performance or change functional claims.
   Report any conflict between the source text and actual definitions instead
   of silently changing balance, code or the source language.
6. Add the target ModLanguageDef to Defs/Languages.xml with the correct folder,
   native label and sort order. Follow the guide's registration example.
   Keep existing language definitions and IDs intact. Add the target language
   name's DefInjected label where needed; do not rename translation keys.
7. Summarize the files changed. Explain that automatic game translation and
   the mod's explicit language override are different paths.
   Do not modify game installations, saves, active mod lists, unrelated files
   or runtime C# behavior.
```

# Translation guide

This file stays beside the language trees so translators can find the loading rules without changing code. Paths below are relative to the mod root unless stated otherwise.

## What belongs where

| Text | Location |
| --- | --- |
| Code-driven UI, messages and failure reasons | `Languages/<folder>/Keyed/*.xml` |
| Definition names, descriptions and job reports | `Languages/<folder>/DefInjected/<Def class>/*.xml` |
| Optional integration text | `Compat/<module>/Languages/<folder>/...` |
| Available languages in this mod's settings | `Defs/Languages.xml` |

Keep the same keys and DefName paths as the source. A filename such as `LocalizationCoverage.xml` has no special meaning; its entries are what matter. For example, English recipe translations may share that file while Korean separates them into `ModularTurrets.xml` and `SpecialistModules.xml`.

Files used by the mod's explicit language loader must sit **directly** inside `Keyed` or `DefInjected/<Def class>`. It reads the XML files in those directories, not arbitrary deeper subfolders. Use relative, ordinary language folders; rooted paths and linked directories are not supported.

Custom Def classes use their full type names as directory names:

- `DefInjected/ModularTurrets.ModLanguageDef`
- `DefInjected/ModularTurrets.ModuleEffectDef`
- CE: `DefInjected/CombatExtended.AmmoSetDef` inside the CE compatibility language tree.

## Add a language

1. Copy the current `Languages/English` tree to the exact target language folder used by RimWorld, then translate the text. Retain existing fields and keys.
2. Add the corresponding target folder to each existing `Compat/*/Languages/English` tree. Currently this includes CE definition text and the Milira/Milira Imperium compatibility messages. Check the repository instead of assuming every module has language files.
3. Add a `ModLanguageDef` to `Defs/Languages.xml` so the mod's language selector can offer it. For French, the entry would be:

```xml
<ModularTurrets.ModLanguageDef>
  <defName>MT_LanguageFrench</defName>
  <label>Français</label>
  <folder>French</folder>
  <gameLanguage>French</gameLanguage>
  <order>40</order>
</ModularTurrets.ModLanguageDef>
```

Choose a unique DefName and an unused order value. `folder` selects the dictionary for the explicit override. `gameLanguage=auto` identifies the special existing automatic entry; a non-automatic value does not change RimWorld's active language.

The Def's label is enough to give the new selector entry a name. To localize that name, use `MT_LanguageFrench.label` in `DefInjected/ModularTurrets.ModLanguageDef/*.xml`. Keep all existing language definitions and the automatic entry.

## Automatic selection, overrides and fallback

**Follow game language** uses RimWorld's active language and translation system. `L.T` first asks the game for a keyed translation. If the game cannot resolve it, the mod falls back to its English dictionary. Definition labels and descriptions in automatic mode use the definitions already translated by RimWorld.

An **explicit mod language** loads English first, then overlays the selected folder's keyed text and definition labels/descriptions. Missing entries therefore retain the English fallback; labels/descriptions without a dictionary entry can fall back to the underlying definition. A missing keyed string with no English entry displays its key.

The explicit override affects the mod UI that uses `L.T`, `L.Label` or `L.Description`. It does not change the game's global language, ordinary game UI, or job report strings that RimWorld itself displays.

Both custom dictionaries and normal game loading respect the active folders selected by `LoadFolders.xml`. CE or other compatibility text should remain in its optional module, and is not read by the custom loader when that folder is inactive.

Restart the game after editing translation XML so RimWorld can reload `DefInjected`. Changing the mod's language selector reloads its own dictionaries, but is not a global translation reload for the game.

## XML and formatting

Use a UTF-8 XML document with a `LanguageData` root:

```xml
<?xml version="1.0" encoding="utf-8"?>
<LanguageData>
  <MT_ExampleHint>First line.&#10;Second line: {0:F1} W&#10;&#10;A separate paragraph.</MT_ExampleHint>
</LanguageData>
```

`&#10;` is the XML character reference for an actual newline. Double it for a paragraph. Do not escape it as `&amp;#10;`, and do not replace it with the visible text `\n`. Older files may use that legacy escape, but new edits should use the XML character reference.

Preserve argument indexes and formatting exactly. Reordering `{0}` and `{1}` for grammar is fine; deleting one, introducing `{2}`, or changing a numeric format is not. Preserve rich-text tags and escape XML-sensitive characters such as `&` and `<` correctly. Keep compact button/status text on one line unless its consumer supports wrapping.

For definition injection, preserve the full field path:

```xml
<LanguageData>
  <MT_AssemblyHaul.reportString>Hauling turret parts and materials.</MT_AssemblyHaul.reportString>
  <MT_AssemblyWork.reportString>Assembling or upgrading a modular turret.</MT_AssemblyWork.reportString>
</LanguageData>
```

These entries belong under `DefInjected/JobDef`. Do not translate DefNames, class namespaces, XML field names, Scribe keys, resource paths or identifiers referenced by code.

## Contributing Translations

Community translations are welcome!

If you encounter translation errors, missing text, or have questions, please open a **GitHub Issue**.
Once your translation is ready, please submit it through a **Pull Request (PR)**.
When submitting a PR, please include the target language and a brief summary of your changes.
AI-assisted translations are welcome, but please review them for accuracy, consistency, and preservation of the original meaning before submission.

Thank you for helping make Modular Turrets accessible to more players!
