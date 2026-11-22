"""Build local PaddleOCR Edition packages; does not publish or upload anything."""
import argparse
import hashlib
import json
import os
import plistlib
import shutil
import struct
import subprocess
import zipfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
ARTIFACTS = ROOT / 'artifacts/paddle-edition'
DIST = ROOT / 'dist/paddle-edition'
TITLE = 'Offline PDF Converter (v5.1)'

def write_icns(icons, destination):
    # Store each PNG in its standard ICNS element so packaging also works without iconutil.
    formats = {
        (16, 1): b'icp4', (32, 1): b'icp5', (128, 1): b'ic07', (256, 1): b'ic08', (512, 1): b'ic09',
        (16, 2): b'ic11', (32, 2): b'ic12', (128, 2): b'ic13', (256, 2): b'ic14', (512, 2): b'ic10',
    }
    chunks = []
    for (pixels, scale), kind in formats.items():
        suffix = '@2x' if scale == 2 else ''
        png = (icons / f'icon_{pixels}x{pixels}{suffix}.png').read_bytes()
        chunks.append(struct.pack('>4sI', kind, len(png) + 8) + png)
    payload = b''.join(chunks)
    destination.write_bytes(struct.pack('>4sI', b'icns', len(payload) + 8) + payload)

def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--target', choices=['mac', 'windows', 'both'], default='both')
    parser.add_argument('--prototype', action='store_true', help='Build v5.1 Prototype packages separately from the release')
    parser.add_argument('--office-macos-app', type=Path, help='LibreOffice app bundle to embed in the Mac prototype')
    parser.add_argument('--office-windows-zip', type=Path, help='LibreOffice runtime ZIP containing program/ and share/')
    args = parser.parse_args()
    if args.target in ('mac', 'both') and not (args.office_macos_app and args.office_macos_app.is_dir()):
        parser.error('--office-macos-app is required for a Mac build')
    if args.target in ('windows', 'both') and not (args.office_windows_zip and args.office_windows_zip.is_file()):
        parser.error('--office-windows-zip is required for a Windows build')
    title = 'Offline PDF Converter (v5.1 Prototype)' if args.prototype else TITLE
    version = '5.1.0'
    bundle_version = '5.1.6' if args.prototype else version
    bundle_id = 'com.offlinepdfconverter.paddle.prototype' if args.prototype else 'com.offlinepdfconverter.paddle'
    artifacts = ARTIFACTS / 'prototype' if args.prototype else ARTIFACTS
    dist = ROOT.parent / 'test-app-v5.1-prototype-6' if args.prototype else DIST
    subprocess.run([os.sys.executable, str(ROOT/'scripts/prepare-ocr-font.py')], check=True)
    dotnet = ROOT / '.dotnet/dotnet'
    if not dotnet.exists(): dotnet = Path(shutil.which('dotnet') or 'dotnet')
    env = dict(os.environ, DOTNET_CLI_HOME=str(artifacts/'dotnet-home'), DOTNET_CLI_TELEMETRY_OPTOUT='1')
    dist.mkdir(parents=True, exist_ok=True)
    for target, rid in [('mac', 'osx-arm64'), ('windows', 'win-x64')]:
        if args.target not in (target, 'both'): continue
        publish = artifacts / ('publish-macos' if target == 'mac' else 'publish-windows')
        command = [str(dotnet), 'publish', 'src/OfflinePDFConverter/OfflinePDFConverter.csproj', '-c', 'Release',
                   '-p:PaddleOcrEdition=true', '-r', rid, '--self-contained', 'true',
                   '-p:PublishSingleFile=true', '-p:IncludeNativeLibrariesForSelfExtract=true',
                   '-p:PublishTrimmed=false', '-p:NuGetAudit=false', '-p:UsedAvaloniaProducts=', '-p:DebugType=none', '-p:DebugSymbols=false', '-o', str(publish)]
        if args.prototype:
            command += ['-p:OpcPrototype=true', '--disable-build-servers', '-p:UseSharedCompilation=false']
        if target == 'windows':
            command.append('-p:PublishProfile=WindowsSingleFile')
            if args.office_windows_zip:
                command.append(f'-p:OpcOfficeEngineArchive={args.office_windows_zip.resolve()}')
        subprocess.run(command, cwd=ROOT, env=env, check=True)
        if target == 'windows':
            subprocess.run([os.sys.executable, str(ROOT/'scripts/verify-published-exe.py'), str(publish)], cwd=ROOT, check=True)
        package = dist / ('macos-arm64' if target == 'mac' else 'windows-x64')
        package.mkdir(parents=True, exist_ok=True)
        if target == 'mac':
            app = package / (title + '.app')
            contents = app / 'Contents'
            binaries = contents / 'MacOS'; resources = contents / 'Resources'
            binaries.mkdir(parents=True, exist_ok=True); resources.mkdir(parents=True, exist_ok=True)
            for file in publish.iterdir():
                if file.is_file(): shutil.copy2(file, binaries/file.name)
            icons = artifacts/'AppIcon.iconset'; icons.mkdir(exist_ok=True)
            for pixels in [16, 32, 128, 256, 512]:
                for scale in [1, 2]:
                    suffix = '@2x' if scale == 2 else ''
                    subprocess.run(['sips','-z',str(pixels*scale),str(pixels*scale),str(ROOT/'src/OfflinePDFConverter/Assets/AppIcon.png'),
                                    '--out',str(icons/f'icon_{pixels}x{pixels}{suffix}.png')],check=True,stdout=subprocess.DEVNULL)
            write_icns(icons, resources/'AppIcon.icns')
            if args.office_macos_app:
                office_engine = resources/'OfficeEngine'/'LibreOffice.app'
                subprocess.run(['ditto', str(args.office_macos_app.resolve()), str(office_engine)], check=True)
                notices = args.office_macos_app.resolve().parent/'LICENSEs'
                if notices.is_dir():
                    subprocess.run(['ditto', str(notices), str(office_engine.parent/'LICENSEs')], check=True)
            with (contents/'Info.plist').open('wb') as stream:
                plistlib.dump({'CFBundleName':title,'CFBundleDisplayName':title,'CFBundleIdentifier':bundle_id,
                               'CFBundleExecutable':'OfflinePDFConverter.PaddleEdition','CFBundlePackageType':'APPL',
                               'CFBundleShortVersionString':version,'CFBundleVersion':bundle_version,'CFBundleIconFile':'AppIcon',
                               'NSHighResolutionCapable':True,'LSMinimumSystemVersion':'11.0'},stream)
            subprocess.run(['codesign','--force','--deep','--sign','-',str(app)],check=True)
            subprocess.run(['codesign','--verify','--deep','--strict',str(app)],check=True)
        else:
            files = list(publish.iterdir())
            if len(files) != 1 or files[0].suffix != '.exe': raise ValueError('Expected one Windows executable')
            shutil.copy2(files[0], package/(title+'.exe'))
        documents = [ROOT/'README.md', ROOT/'LICENSE', ROOT/'THIRD_PARTY_LICENSES.md'] + [ROOT/'docs'/name for name in
                     ['MANUAL.md', 'IMPROVEMENTS.md', 'PADDLE_EDITION.md', 'SEARCHABLE_PDF.md', 'RELEASE_DETAILS_v5.1.0.md']]
        if args.prototype:
            documents = [ROOT/'THIRD_PARTY_LICENSES.md']
        for source in documents:
            shutil.copy2(source, package/source.name)
        shutil.copy2(ROOT/'src/OfflinePDFConverter/Assets/Fonts/OFL.txt', package/'OFL-ZenKakuGothicNew.txt')
        shutil.copy2(ROOT/'src/OfflinePDFConverter/Assets/Fonts/OFL-NotoSansJP.txt', package/'OFL-NotoSansJP.txt')
        archive = dist/(title+'-'+('macOS-arm64' if target == 'mac' else 'Windows-x64')+'.zip')
        deliverable = package / (title + ('.app' if target == 'mac' else '.exe'))
        if target == 'mac' and args.office_macos_app:
            subprocess.run(['ditto', '-c', '-k', '--keepParent', str(deliverable), str(archive)], check=True)
            files = []
            zip_mode = 'a'
        else:
            files = list(deliverable.rglob('*')) if target == 'mac' else [deliverable]
            zip_mode = 'w'
        files += [package/source.name for source in documents] + [package/'OFL-ZenKakuGothicNew.txt', package/'OFL-NotoSansJP.txt']
        with zipfile.ZipFile(archive, zip_mode, zipfile.ZIP_DEFLATED, compresslevel=6) as bundle:
            for file in sorted(files):
                if file.is_file(): bundle.write(file, file.relative_to(package))
        with archive.open('rb') as stream:
            sha256 = hashlib.sha256()
            for block in iter(lambda: stream.read(1024 * 1024), b''):
                sha256.update(block)
        digest = sha256.hexdigest()
        archive.with_suffix('.zip.sha256.txt').write_text(digest+'  '+archive.name+'\n')
        print(json.dumps({'package':str(archive),'bytes':archive.stat().st_size,'sha256':digest}),flush=True)

if __name__ == '__main__': main()
