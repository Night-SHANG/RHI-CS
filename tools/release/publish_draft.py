"""Package runner outputs and upload with GitHub CLI; never publish the draft."""
import hashlib
import json
import os
from pathlib import Path
import subprocess
import sys
import zipfile


def run(*args):
    return subprocess.check_output(args, text=True, encoding="utf-8").strip()


def package(destination, inputs, base):
    files = sorted({file for path in inputs if path.exists()
                    for file in (path.rglob("*") if path.is_dir() else [path]) if file.is_file()})
    if not files:
        return None
    with zipfile.ZipFile(destination, "x", zipfile.ZIP_DEFLATED) as archive:
        for file in files:
            archive.write(file, file.relative_to(base).as_posix())
    return destination


def main():
    if os.environ.get("GITHUB_ACTIONS") != "true" or os.environ.get("GITHUB_EVENT_NAME") == "pull_request":
        raise RuntimeError("This uploader runs only in the repository's trusted Actions jobs.")
    repo = os.environ["GITHUB_REPOSITORY"]
    if repo != "Night-SHANG/RHI-CS":
        raise RuntimeError("Unexpected upload repository.")
    kind = sys.argv[1]
    if kind not in ("development", "performance"):
        raise ValueError("Unknown output kind.")
    sha = os.environ["GITHUB_SHA"]
    status = os.environ["RHI_BUILD_STATUS"]
    run_id, attempt = os.environ["GITHUB_RUN_ID"], os.environ["GITHUB_RUN_ATTEMPT"]
    tag = f"ci-{kind}-{run_id}-{attempt}"
    root = Path(os.environ["GITHUB_WORKSPACE"])
    artifacts = root / "artifacts"
    output = artifacts / "release-assets" / f"{kind}-{attempt}"
    output.mkdir(parents=True, exist_ok=False)
    assets = []
    if kind == "development":
        assets.append(package(output / "RHI-CS-ui-evidence.zip", artifacts.glob("ui*"), artifacts))
        assets.append(package(output / "RHI-CS-build-diagnostics.zip",
                              [artifacts / "tests", artifacts / "winui.binlog"], artifacts))
        if status == "success":
            app = artifacts / "app"
            if not (app / "RHI-CS.exe").is_file() or (app / "Data").exists():
                raise RuntimeError("Missing application or test Data remains in the package.")
            assets.append(package(output / f"RHI-CS-win-x64-{sha[:7]}.zip", [app], app))
            source = output / f"RHI-CS-source-{sha[:7]}.zip"
            run("git", "-C", str(root), "archive", "--format=zip", f"--output={source}", sha)
            assets.append(source)
    else:
        report = artifacts / "performance" / "current.json"
        if report.is_file():
            assets.append(report)
    assets = [asset for asset in assets if asset is not None]
    if not assets:
        print("No generated outputs are available; no draft created.")
        return
    checksums = output / "SHA256SUMS.txt"
    lines = []
    for file in assets:
        with file.open("rb") as stream:
            lines.append(f"{hashlib.file_digest(stream, 'sha256').hexdigest()}  {file.name}\n")
    checksums.write_text("".join(lines), encoding="utf-8")
    assets.append(checksums)
    notes = output / "notes.md"
    notes.write_text(f"Commit: {sha}\n\nWorkflow: https://github.com/{repo}/actions/runs/{run_id}\n\n"
                     f"Result before upload: {status}.\n\n"
                     "Unpublished development evidence. Application packages are included only after all build and UI checks pass.\n",
                     encoding="utf-8")
    run("gh", "release", "create", tag, "--repo", repo, "--target", sha, "--draft", "--prerelease",
        "--latest=false", "--title", f"RHI-CS {kind} {sha[:7]} ({status})", "--notes-file", str(notes),
        *map(str, assets))
    release = json.loads(run("gh", "release", "view", tag, "--repo", repo,
                             "--json", "isDraft,targetCommitish,assets,url"))
    if not release["isDraft"] or release["targetCommitish"] != sha:
        raise RuntimeError("Release draft/commit verification failed.")
    actual = {asset["name"]: asset["size"] for asset in release["assets"]}
    if any(actual.get(file.name) != file.stat().st_size for file in assets):
        raise RuntimeError("Uploaded assets are incomplete.")
    summary = f"Draft Release: {release['url']}\nCommit: {sha}\nAssets: {', '.join(actual)}\n"
    print(summary)
    with open(os.environ["GITHUB_STEP_SUMMARY"], "a", encoding="utf-8") as stream:
        stream.write(summary)


if __name__ == "__main__":
    main()
