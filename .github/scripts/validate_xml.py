#!/usr/bin/env python3
"""Validate ModularTurrets XML and localization without game dependencies.

Missing translation keys are informational: the mod has English fallbacks.
"""
from collections import Counter, defaultdict
from pathlib import Path
from string import Formatter
import subprocess
import sys
import xml.etree.ElementTree as ET

ROOT = Path("ModularTurrets")
errors = []
warnings = []
translations = {}  # (module, language, Keyed or DefInjected/<class>, key) -> (text, path)
scopes = defaultdict(dict)
formatter = Formatter()


def error(message):
    errors.append(message)


def placeholders(text, path, key):
    try:
        return Counter(
            (field, spec, conversion)
            for _, field, spec, conversion in formatter.parse(text)
            if field is not None
        )
    except ValueError as exc:
        error(f"{path}: malformed format string in {key}: {exc}")
        return None


def translation_scope(path):
    parts = path.parts
    if len(parts) >= 4 and parts[:2] == ("ModularTurrets", "Languages"):
        module, rest = "Core", parts[2:]
    elif len(parts) >= 6 and parts[0] == "ModularTurrets" and parts[1] == "Compat" and parts[3] == "Languages":
        module, rest = parts[2], parts[4:]
    else:
        return None
    language, kind, *tail = rest
    if kind == "Keyed" and len(tail) == 1:
        return module, language, "Keyed"
    if kind == "DefInjected" and len(tail) == 2:
        return module, language, f"DefInjected/{tail[0]}"
    error(f"{path}: invalid language file placement")
    return None


files = sorted(ROOT.rglob("*.xml"))
if not files:
    error("No XML files found under ModularTurrets")

for path in files:
    if path.is_symlink():
        error(f"{path}: symbolic-link XML is not allowed")
        continue
    try:
        root = ET.parse(path).getroot()
    except (ET.ParseError, OSError) as exc:
        error(f"{path}: XML parse failed: {exc}")
        continue
    scope = translation_scope(path)
    if scope is None:
        continue
    if root.tag != "LanguageData":
        error(f"{path}: expected LanguageData root, found {root.tag}")
        continue
    for entry in root:
        if not isinstance(entry.tag, str):
            continue  # XML comment
        if len(entry):
            error(f"{path}: {entry.tag} contains nested XML elements")
            continue
        identity = (*scope, entry.tag)
        if identity in translations:
            error(f"Duplicate translation key {identity}: {translations[identity][1]} and {path}")
            continue
        value = entry.text or ""
        translations[identity] = (value, path)
        scopes[scope][entry.tag] = value
        placeholders(value, path, entry.tag)

# Compare *keys* by Def class / Keyed scope, not by filename.
for (module, language, group), dictionary in sorted(scopes.items()):
    if language == "English":
        continue
    reference = scopes.get((module, "English", group))
    if reference is None:
        error(f"{module}/{language}/{group}: English reference scope is missing")
        continue
    for key in dictionary.keys() & reference.keys():
        src = placeholders(reference[key], "English", key)
        dest = placeholders(dictionary[key], f"{module}/{language}/{group}", key)
        if src is not None and dest is not None and src != dest:
            error(f"{module}/{language}/{group}: placeholder mismatch for {key}: {src} -> {dest}")
    missing = reference.keys() - dictionary.keys()
    extra = dictionary.keys() - reference.keys()
    if missing:
        warnings.append(f"{module}/{language}/{group}: {len(missing)} untranslated keys (English fallback)")
    if extra:
        warnings.append(f"{module}/{language}/{group}: {len(extra)} keys not in English (review separately)")

# Completely absent class scopes are also valid English fallbacks, but visible.
modules = {module for module, language, group in scopes}
for module in sorted(modules):
    english_groups = {group for m, lang, group in scopes if m == module and lang == "English"}
    languages = {lang for m, lang, group in scopes if m == module and lang != "English"}
    for language in sorted(languages):
        for group in sorted(english_groups):
            if (module, language, group) not in scopes:
                warnings.append(f"{module}/{language}/{group}: entire scope untranslated (English fallback)")

# Each Core language folder must be registered in Defs/Languages.xml.
languages_file = ROOT / "Defs" / "Languages.xml"
try:
    definitions = ET.parse(languages_file).getroot()
    orders, defnames, folders = set(), set(), set()
    for entry in definitions.findall("ModularTurrets.ModLanguageDef"):
        name = entry.findtext("defName")
        order = entry.findtext("order")
        folder = entry.findtext("folder")
        if not name or name in defnames:
            error(f"{languages_file}: missing/duplicate defName {name!r}")
        defnames.add(name)
        if order is None or not order.isdecimal() or int(order) in orders:
            error(f"{languages_file}: invalid/duplicate order {order!r}")
        elif order is not None:
            orders.add(int(order))
        if folder:
            if folder in folders or not folder.replace("-", "").replace("_", "").isalnum():
                error(f"{languages_file}: invalid/duplicate language folder {folder!r}")
            folders.add(folder)
            if not (ROOT / "Languages" / folder).is_dir():
                error(f"{languages_file}: language folder {folder!r} missing")
    found = {p.name for p in (ROOT / "Languages").iterdir() if p.is_dir()}
    for folder in sorted(found - folders):
        error(f"Core language folder is not registered: {folder}")
except (ET.ParseError, OSError) as exc:
    error(f"{languages_file}: cannot validate language definitions: {exc}")

# Source repositories should not contain compiled binaries, saves or secrets.
result = subprocess.run(["git", "ls-files", "-z"], capture_output=True, check=True)
for raw in result.stdout.decode("utf-8", "replace").split("\0"):
    if not raw:
        continue
    path = Path(raw)
    if path.suffix.lower() in {".dll", ".pdb", ".exe", ".pem", ".key", ".rws", ".zip"}:
        error(f"Forbidden tracked file type: {raw}")
    if any(part == ".env" or part.startswith(".env.") for part in path.parts):
        error(f"Tracked environment file: {raw}")

for message in warnings:
    print("WARNING:", message)
for message in errors:
    print("ERROR:", message)
print(f"Scanned {len(files)} XML files, {len(translations)} translation keys; "
      f"{len(errors)} error(s), {len(warnings)} warning(s).")
sys.exit(1 if errors else 0)
