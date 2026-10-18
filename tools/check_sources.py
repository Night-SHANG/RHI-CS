"""Read-only structural checks; never substitutes for C# compilation or tests."""
import json
import re
import sys
import argparse
import xml.etree.ElementTree as ET
from pathlib import Path

sys.stdout.reconfigure(encoding="utf-8")
root = Path(__file__).resolve().parents[1]
parser = argparse.ArgumentParser()
parser.add_argument("--published", type=Path)
args = parser.parse_args()
errors = []
checked = 0
for folder in (root / "src", root / "tests"):
    for path in folder.rglob("*"):
        if any(p in {"obj", "bin"} for p in path.parts) or not path.is_file():
            continue
        text = path.read_text(encoding="utf-8-sig")
        checked += 1
        if "\ufffd" in text:
            errors.append(f"replacement character: {path}")
        if path.suffix in {".xaml", ".csproj", ".manifest", ".resw"}:
            try:
                ET.fromstring(text)
            except ET.ParseError as exc:
                errors.append(f"XML: {path}: {exc}")
        if path.suffix == ".csproj":
            for ref in ET.fromstring(text).iter("ProjectReference"):
                if not (path.parent / ref.attrib["Include"]).resolve().is_file():
                    errors.append(f"missing project: {ref.attrib['Include']}")

def read_catalog(path):
    values = {}
    for node in ET.fromstring(path.read_text(encoding="utf-8-sig")).findall("data"):
        key = node.attrib.get("name", "")
        if not key or key in values:
            errors.append(f"missing or duplicate locale key: {path}: {key}")
        values[key] = node.findtext("value", default="")
    return values

loc = root / "src/Rhi.App/Strings"
catalogs = {path.parent.name: read_catalog(path) for path in sorted(loc.glob("*/Resources.resw"))}
for required in ("en-US", "zh-CN"):
    if required not in catalogs:
        errors.append(f"missing required locale: {required}")
en = catalogs.get("en-US", {})
for language, values in catalogs.items():
    if values.keys() != en.keys():
        errors.append(f"locale key mismatch: {language}: {values.keys() ^ en.keys()}")
    for key, value in values.items():
        fields = lambda text: set(re.findall(r"(?<!\{)\{(\d+)(?:,[^}:]+)?(?::[^}]+)?\}(?!\})", text))
        if fields(value) != fields(en.get(key, "")):
            errors.append(f"placeholder mismatch: {language}: {key}")
        if not value.strip():
            errors.append(f"empty locale: {language}: {key}")

if args.published is not None:
    for language, values in catalogs.items():
        path = args.published / "Strings" / language / "Resources.resw"
        if not path.is_file():
            errors.append(f"missing published locale: {path}")
        elif read_catalog(path) != values:
            errors.append(f"published locale differs from source: {language}")
    if not (args.published / "WinUI3Localizer.dll").is_file():
        errors.append("missing published WinUI3Localizer.dll")
    if not (args.published / "Licenses/WinUI3Localizer.txt").is_file():
        errors.append("missing published WinUI3Localizer license")
for path in (root / "src").rglob("*.cs"):
    text = path.read_text(encoding="utf-8")
    for key in re.findall(r'(?:Strings\.(?:Get|Format)|Ui\.(?:Label|Input|Choice)|c\.(?:Button|Action|Confirm|Message)|context\.(?:Button|Action|Confirm|Message)|DeploymentException)\("([A-Za-z][A-Za-z0-9]+)"', text):
        if key not in en:
            errors.append(f"untranslated key {key}: {path.name}")
    if "Vendor" not in path.parts:
        literals = re.findall(r'(?:Text|Content|Header|PlaceholderText|Title)\s*=\s*"([^"\n]+)"', text)
        literals += re.findall(r'Ui\.Text\("([^"\n]+)"', text)
        for literal in literals:
            if literal != "RHI-CS":
                errors.append(f"hard-coded UI text: {path.name}: {literal}")
for path in (root / "src/Rhi.App").rglob("*.xaml"):
    for element in ET.fromstring(path.read_text(encoding="utf-8")).iter():
        for name in ("Text", "Content", "Header", "PlaceholderText", "Title"):
            value = element.attrib.get(name, "")
            if value and value != "RHI-CS" and not value.startswith("{"):
                errors.append(f"hard-coded XAML text: {path.name}: {value}")
for folder in (root / "src").rglob("*"):
    if folder.is_dir() and not any(p in {"obj", "bin"} for p in folder.parts):
        files = list(folder.glob("*.cs"))
        if len(files) > 8:
            errors.append(f"directory decomposition needed: {folder}: {len(files)}")
print(f"Read-only checks: {checked} source/config files, {len(en)} locale keys across {len(catalogs)} languages")
for error in errors:
    print(error)
if errors:
    sys.exit(1)
print("PASS (structure and resources only; C# build/runtime not checked)")
