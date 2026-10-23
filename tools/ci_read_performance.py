"""Read the one completed synthetic benchmark; print numeric summaries only."""
import hashlib
import json
import os
import subprocess

if os.environ.get("GITHUB_ACTIONS") != "true" or os.environ.get("GITHUB_REPOSITORY") != "Night-SHANG/RHI-CS":
    raise RuntimeError("Disposable repository runner only.")
raw = subprocess.check_output([
    "gh", "api", "repos/Night-SHANG/RHI-CS/releases/assets/668003286",
    "-H", "Accept: application/octet-stream",
])
if len(raw) != 102271 or hashlib.sha256(raw).hexdigest() != "8242d4729618381d588d34bee3b185f6b1d8ca5a1e85a2dcbb5d914dde9a576d":
    raise RuntimeError("Unexpected benchmark asset.")
report = json.loads(raw)
if report["status"] != "success" or report["candidateRevision"] != "2062f411445d742049d4961b80886da1953d41d8":
    raise RuntimeError("Unexpected benchmark result.")
for item in report["comparisons"]:
    print("PERFORMANCE_SUMMARY:" + json.dumps({
        "stage": item["stage"],
        "baseline": item["baseline"],
        "candidate": item["candidate"],
        "changePercent": item["candidateChangePercent"],
    }, separators=(",", ":")))
