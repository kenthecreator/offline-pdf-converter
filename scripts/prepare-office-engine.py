"""Extract the pinned LibreOffice runtime for release packaging, without installing it."""
import argparse
import hashlib
import shutil
import subprocess
import tempfile
import urllib.request
import zipfile
from pathlib import Path

VERSION = "26.8.3"
BUILD_ID = "861effc5289d539f15c4c18d1ba8579ffd78728b"
INSTALLERS = {
    "mac": (
        "mac/aarch64/LibreOffice_26.8.3_MacOS_aarch64.dmg",
        "de943203795e5057785f65515063e2db5eb60b0bb6d8e2534fec59e5cc5fb988",
    ),
    "windows": (
        "win/x86_64/LibreOffice_26.8.3_Win_x86-64.msi",
        "0e6918c273eabbdbbd800ee0dacbbf130a14c13c2e4cec861b06dd59e015bf94",
    ),
}


def validate_version(path):
    settings = dict(
        line.split("=", 1) for line in path.read_text(encoding="utf-8-sig").splitlines() if "=" in line
    )
    if settings.get("buildid") != BUILD_ID:
        raise ValueError(f"Unexpected LibreOffice build ID in {path}")


def prepare_mac(installer, output, work):
    mount = work / "mounted"
    mount.mkdir()
    subprocess.run(["hdiutil", "attach", "-readonly", "-nobrowse", "-mountpoint",
                    str(mount), str(installer)], check=True)
    try:
        app = mount / "LibreOffice.app"
        validate_version(app / "Contents/Resources/versionrc")
        subprocess.run(["ditto", str(app), str(output / "LibreOffice.app")], check=True)
        subprocess.run(["ditto", str(mount / "LICENSEs"), str(output / "LICENSEs")], check=True)
    finally:
        subprocess.run(["hdiutil", "detach", str(mount)], check=True)


def prepare_windows(installer, output, work):
    unpacked = work / "unpacked"
    log = work / "extract.log"
    result = subprocess.run(
        ["msiexec.exe", "/a", str(installer), "/qn", "/norestart",
         f"TARGETDIR={unpacked}", "/l*v", str(log)], check=False
    )
    if result.returncode:
        raise RuntimeError(f"LibreOffice administrative extraction failed ({result.returncode}): "
                           + log.read_text(encoding="utf-16", errors="replace")[-4000:])
    roots = [
        version.parent.parent for version in unpacked.rglob("version.ini")
        if version.parent.name == "program" and (version.parent.parent / "share").is_dir()
    ]
    if len(roots) != 1:
        raise ValueError(f"Expected one LibreOffice runtime after MSI extraction, found {len(roots)}")
    root = roots[0]
    validate_version(root / "program/version.ini")
    if not any((root / "program" / name).is_file() for name in ("mergedlo.dll", "sofficeapp.dll")):
        raise ValueError("LibreOfficeKit library was not found in the Windows runtime")
    with zipfile.ZipFile(output / "LibreOffice-win-x64.zip", "w", zipfile.ZIP_DEFLATED, compresslevel=3) as archive:
        for member in sorted(root.rglob("*")):
            if member.is_file() and member.suffix.lower() != ".msi":
                archive.write(member, member.relative_to(root))


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--target", choices=INSTALLERS, required=True)
    parser.add_argument("--output", required=True, type=Path)
    parser.add_argument("--installer", type=Path, help="Use an already downloaded official installer")
    args = parser.parse_args()
    output = args.output.resolve()
    output.mkdir(parents=True, exist_ok=True)
    relative, expected = INSTALLERS[args.target]
    with tempfile.TemporaryDirectory(prefix="opc-office-engine-") as temporary:
        work = Path(temporary)
        installer = args.installer.resolve() if args.installer else work / Path(relative).name
        if not args.installer:
            url = f"https://download.documentfoundation.org/libreoffice/stable/{VERSION}/{relative}"
            with urllib.request.urlopen(url, timeout=90) as download, installer.open("wb") as file:
                shutil.copyfileobj(download, file)
        digest = hashlib.sha256()
        with installer.open("rb") as stream:
            for block in iter(lambda: stream.read(1024 * 1024), b""):
                digest.update(block)
        if digest.hexdigest() != expected:
            raise ValueError(f"LibreOffice installer checksum mismatch: {installer}")
        (prepare_mac if args.target == "mac" else prepare_windows)(installer, output, work)


if __name__ == "__main__":
    main()
