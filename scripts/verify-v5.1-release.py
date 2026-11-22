"""Check that v5.1 metadata, download links and release archives agree."""
import argparse
import hashlib
import plistlib
import xml.etree.ElementTree as ET
import zipfile
from html.parser import HTMLParser
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
VERSION = "5.1.0"
TITLE = "Offline PDF Converter (v5.1)"
REPO = "https://github.com/kenthecreator/offline-pdf-converter"
TARGETS = ("Windows-x64", "macOS-arm64")


class Links(HTMLParser):
    def __init__(self):
        super().__init__()
        self.links = set()

    def handle_starttag(self, tag, attrs):
        if tag == "a":
            self.links.add(dict(attrs).get("href"))


def verify_metadata():
    project = ET.parse(ROOT / "src/OfflinePDFConverter/OfflinePDFConverter.csproj")
    assert project.findtext(".//Version") == VERSION
    for field in ("AssemblyVersion", "FileVersion"):
        assert project.findtext(f".//{field}") == VERSION + ".0"
    assert project.findtext(".//Product") == TITLE
    identity = (ROOT / "src/OfflinePDFConverter/Services/AppIdentity.cs").read_text(encoding="utf-8")
    assert 'HeaderSuffix = " (v5.1)"' in identity
    assert 'ExpectedVersion = "5.1.0.0"' in identity
    assert f"TITLE = '{TITLE}'" in (ROOT / "scripts/build-paddle-edition.py").read_text()
    notes = ROOT / "docs" / f"RELEASE_DETAILS_v{VERSION}.md"
    assert notes.read_text(encoding="utf-8").startswith("# " + TITLE)
    page = (ROOT / "docs/download-page/index.html").read_text(encoding="utf-8")
    parser = Links()
    parser.feed(page)
    for target in TARGETS:
        url = f"{REPO}/releases/download/v{VERSION}/Offline.PDF.Converter.v5.1.-{target}.zip"
        assert {url, url + ".sha256.txt"} <= parser.links
    assert f"{REPO}/releases/tag/v{VERSION}" in parser.links
    assert f"{REPO}/blob/v{VERSION}/docs/MANUAL.md" in parser.links
    assert "v5.0" not in page
    assert 'href="app-icon-v5.1.png"' in page and 'src="app-icon-v5.1.png"' in page
    assert (ROOT / "docs/download-page/app-icon-v5.1.png").read_bytes().startswith(b"\x89PNG\r\n\x1a\n")
    print("v5.1 version fields, release notes and Pages links verified")


def verify_archive(folder, target):
    matches = list(folder.rglob(f"{TITLE}-{target}.zip"))
    assert len(matches) == 1, f"Missing or ambiguous v5.1 archive for {target}"
    archive = matches[0]
    expected = archive.with_suffix(".zip.sha256.txt").read_text().split()[0]
    digest = hashlib.sha256()
    with archive.open("rb") as handle:
        for block in iter(lambda: handle.read(1024 * 1024), b""):
            digest.update(block)
    assert digest.hexdigest() == expected, archive.name
    with zipfile.ZipFile(archive) as bundle:
        assert bundle.testzip() is None, archive.name
        names = set(bundle.namelist())
        assert {f"RELEASE_DETAILS_v{VERSION}.md", "LICENSE", "THIRD_PARTY_LICENSES.md",
                "OFL-NotoSansJP.txt", "OFL-ZenKakuGothicNew.txt", "MANUAL.md", "IMPROVEMENTS.md"} <= names
        if target == "Windows-x64":
            assert {name for name in names if name.lower().endswith((".exe", ".dll"))} == {TITLE + ".exe"}
        else:
            contents = TITLE + ".app/Contents/"
            info = plistlib.loads(bundle.read(contents + "Info.plist"))
            assert info["CFBundleName"] == info["CFBundleDisplayName"] == TITLE
            assert info["CFBundleVersion"] == info["CFBundleShortVersionString"] == VERSION
            assert info["CFBundleIconFile"] == "AppIcon"
            assert contents + "Resources/AppIcon.icns" in names
            assert contents + "Resources/OfficeEngine/LibreOffice.app/Contents/Frameworks/libmergedlo.dylib" in names
            assert any(name.startswith(contents + "Resources/OfficeEngine/LICENSEs/") for name in names)
    print("Verified", archive.name)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--archives", type=Path)
    parser.add_argument("--target", choices=TARGETS, action="append")
    args = parser.parse_args()
    verify_metadata()
    if args.archives:
        for target in args.target or TARGETS:
            verify_archive(args.archives, target)


if __name__ == "__main__":
    main()
