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

# Check the actual shared foreground tokens against every surface they appear on.
theme_xml = ET.parse(root / "src/Rhi.App/Styles/Theme.xaml")
xaml_key = "{http://schemas.microsoft.com/winfx/2006/xaml}Key"
def luminance(hex_color):
    rgb = [int(hex_color[i:i + 2], 16) / 255 for i in (1, 3, 5)]
    linear = [v / 12.92 if v <= 0.04045 else ((v + 0.055) / 1.055) ** 2.4 for v in rgb]
    return sum(v * weight for v, weight in zip(linear, (0.2126, 0.7152, 0.0722)))

for dictionary in theme_xml.getroot().iter():
    theme = dictionary.get(xaml_key)
    if theme not in {"Light", "Dark", "Default"}:
        continue
    colors = {brush.get(xaml_key): brush.get("Color") for brush in dictionary}
    for foreground in ("TextBrush", "SecondaryTextBrush", "SuccessBrush", "WarningBrush", "ErrorBrush"):
        for background in ("CanvasBrush", "PanelBrush", "CardBrush"):
            if foreground not in colors or background not in colors:
                errors.append(f"missing theme token: {theme}: {foreground}/{background}")
                continue
            a, b = sorted((luminance(colors[foreground]), luminance(colors[background])))
            contrast = (b + 0.05) / (a + 0.05)
            if contrast < 4.5:
                errors.append(f"insufficient text contrast: {theme}: {foreground}/{background}: {contrast:.2f}")

# Windows PowerShell 5.1 interprets BOM-less script files as the system code page.
for path in (root / "tools").glob("*.ps1"):
    data = path.read_bytes()
    if not data.startswith(b"\xef\xbb\xbf") and not data.isascii():
        errors.append(f"PowerShell script needs UTF-8 BOM or ASCII-only source: {path}")
def is_source_file(path):
    return path.is_file() and not any(part in {"obj", "bin"} for part in path.relative_to(root).parts)

for folder in (root / "src", root / "tests"):
    for path in folder.rglob("*"):
        if not is_source_file(path):
            continue
        if path.suffix.lower() in {".ico", ".png"}:
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
    if not (args.published / "Assets/app.ico").is_file():
        errors.append("missing published application icon")
    xaml_resources = sorted(str(path.relative_to(args.published))
                            for path in args.published.rglob("*")
                            if path.suffix.lower() in {".xbf", ".pri"})
    print("Published XAML resources: " + ", ".join(xaml_resources))
    app_project = ET.parse(root / "src/Rhi.App/Rhi.App.csproj").getroot()
    resource_index = app_project.findtext("./PropertyGroup/AssemblyName") + ".pri"
    if not (args.published / resource_index).is_file():
        errors.append(f"missing published application resource index: {resource_index}")
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
    if not (args.published / "Licenses/ReShade.txt").is_file():
        errors.append("missing published ReShade license")
for path in (root / "src").rglob("*.cs"):
    if not is_source_file(path):
        continue
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
    if not is_source_file(path):
        continue
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
