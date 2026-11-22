"""Promote the verified v5.1 GitHub draft, then update the download page."""
import argparse
import base64
import json
import subprocess
import tempfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
REPO = "kenthecreator/offline-pdf-converter"
TAG = "v5.1.0"
TITLE = "Offline PDF Converter (v5.1)"
PLATFORMS = ("Windows-x64", "macOS-arm64")
REPORTS = ("macos-features", "macos-gui", "windows2022-features", "windows2022-gui",
           "windows2025-features", "windows2025-gui")


def gh(*args, data=None):
    return subprocess.run(["gh", *args], capture_output=True, input=data, check=True, text=True).stdout


def api(path, *args, data=None):
    return json.loads(gh("api", f"repos/{REPO}/{path}", *args, data=data))


def fetch_assets(release, directory):
    assets = {a["name"]: a for a in release["assets"] if a["state"] == "uploaded"}
    archives = [f"Offline.PDF.Converter.v5.1.-{target}.zip" for target in PLATFORMS]
    checksums = [name + ".sha256.txt" for name in archives]
    reports = [f"verification-{name}.json" for name in REPORTS]
    required = set(archives + checksums + reports + ["office-parity.json"])
    if required - assets.keys():
        raise ValueError(f"Missing upload: {sorted(required - assets.keys())}")
    for name in checksums + reports + ["office-parity.json"]:
        data = gh("api", f"repos/{REPO}/releases/assets/{assets[name]['id']}",
                  "-H", "Accept: application/octet-stream")
        (directory / name).write_text(data, encoding="utf-8")
    for archive, checksum in zip(archives, checksums):
        fields = (directory / checksum).read_text().split()
        if len(fields) != 2 or fields[1] != archive or "sha256:" + fields[0] != assets[archive]["digest"]:
            raise ValueError(f"Uploaded archive digest differs from {checksum}")
    for report in reports:
        if json.loads((directory / report).read_text(encoding="utf-8-sig"))["passed"] is not True:
            raise ValueError(f"Failed verification report: {report}")
    return json.loads((directory / "office-parity.json").read_text(encoding="utf-8"))


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--publish", action="store_true", help="Publish the release and update gh-pages")
    args = parser.parse_args()
    page = ROOT / "docs/download-page/index.html"
    releases = gh("api", f"repos/{REPO}/releases", "--paginate", "--jq",
                  f'.[] | select(.tag_name == "{TAG}") | .id').split()
    if len(releases) != 1:
        raise ValueError(f"Expected one verified {TAG} release draft")
    release = api(f"releases/{releases[0]}")
    if release["name"] != TITLE or release["prerelease"]:
        raise ValueError("Unexpected release title or prerelease state")
    with tempfile.TemporaryDirectory(prefix="opc-release-check-") as temporary:
        parity = fetch_assets(release, Path(temporary))
    target = release["target_commitish"]
    if parity["passed"] is not True or parity["commit"] != target:
        raise ValueError("No Office parity evidence for the release commit")
    # Publish only the Pages revision stored in the exact commit verified by CI.
    verified_file = api(f"contents/docs/download-page/index.html?ref={target}")
    if base64.b64decode(verified_file["content"]) != page.read_bytes():
        raise ValueError("Local download page differs from the verified release commit")
    current = api("contents/index.html?ref=gh-pages")
    if not args.publish:
        print(f"{TAG} assets and target verified. Pages would be updated from {target}.")
        return

    if release["draft"]:
        gh("release", "edit", TAG, "--repo", REPO, "--draft=false", "--latest")
    public = api("releases/latest")
    tag = api(f"git/ref/tags/{TAG}")
    if public["tag_name"] != TAG or tag["object"]["sha"] != target:
        raise ValueError(f"The verified {TAG} is not the latest public release")
    if base64.b64decode(current["content"]) != page.read_bytes():
        api("contents/index.html", "--method", "PUT", "--input", "-", data=json.dumps({
            "message": f"Point the official download page at v5.1 ({target[:12]})",
            "branch": "gh-pages",
            "sha": current["sha"],
            "content": base64.b64encode(page.read_bytes()).decode("ascii"),
        }))
    print(f"{TAG} published and gh-pages updated. Confirm the public Pages deployment.")


if __name__ == "__main__":
    main()
