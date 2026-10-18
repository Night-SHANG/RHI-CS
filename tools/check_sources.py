"""Read-only structural checks; never substitutes for C# compilation or tests."""
import json
import re
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

sys.stdout.reconfigure(encoding="utf-8")
root = Path(__file__).resolve().parents[1]
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
        if path.suffix in {".xaml", ".csproj", ".manifest"}:
            try:
                ET.fromstring(text)
            except ET.ParseError as exc:
                errors.append(f"XML: {path}: {exc}")
        if path.suffix == ".csproj":
            for ref in ET.fromstring(text).iter("ProjectReference"):
                if not (path.parent / ref.attrib["Include"]).resolve().is_file():
                    errors.append(f"missing project: {ref.attrib['Include']}")

loc = root / "src/Rhi.App/Localization"
zh = json.loads((loc / "zh-CN.json").read_text(encoding="utf-8"))
en = json.loads((loc / "en-US.json").read_text(encoding="utf-8"))
if zh.keys() != en.keys():
    errors.append(f"locale key mismatch: {zh.keys() ^ en.keys()}")
for key in zh:
    if set(re.findall(r"\{\d+\}", zh[key])) != set(re.findall(r"\{\d+\}", en.get(key, ""))):
        errors.append(f"placeholder mismatch: {key}")
    if not zh[key] or not en.get(key):
        errors.append(f"empty locale: {key}")
for path in (root / "src").rglob("*.cs"):
    text = path.read_text(encoding="utf-8")
    for key in re.findall(r'(?:Strings\.(?:Get|Format)|Ui\.(?:Label|Input|Choice)|c\.(?:Button|Action|Confirm|Message)|context\.(?:Button|Action|Confirm|Message)|DeploymentException)\("([A-Za-z][A-Za-z0-9]+)"', text):
        if key not in zh:
            errors.append(f"untranslated key {key}: {path.name}")
for folder in (root / "src").rglob("*"):
    if folder.is_dir() and not any(p in {"obj", "bin"} for p in folder.parts):
        files = list(folder.glob("*.cs"))
        if len(files) > 8:
            errors.append(f"directory decomposition needed: {folder}: {len(files)}")
print(f"Read-only checks: {checked} source/config files, {len(zh)} paired locale keys")
for error in errors:
    print(error)
if errors:
    sys.exit(1)
print("PASS (structure and resources only; C# build/runtime not checked)")
