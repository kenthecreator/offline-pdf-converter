"""Build local PaddleOCR Edition packages; does not publish or upload anything."""
import argparse
import hashlib
import json
import os
import plistlib
import shutil
import subprocess
import zipfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
ARTIFACTS = ROOT / 'artifacts/paddle-edition'
DIST = ROOT / 'dist/paddle-edition'
TITLE = 'Offline PDF Converter (v4.1)'

def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--target', choices=['mac', 'windows', 'both'], default='both')
    args = parser.parse_args()
    dotnet = ROOT / '.dotnet/dotnet'
    if not dotnet.exists(): dotnet = Path(shutil.which('dotnet') or 'dotnet')
    env = dict(os.environ, DOTNET_CLI_HOME=str(ARTIFACTS/'dotnet-home'), DOTNET_CLI_TELEMETRY_OPTOUT='1')
    DIST.mkdir(parents=True, exist_ok=True)
    for target, rid in [('mac', 'osx-arm64'), ('windows', 'win-x64')]:
        if args.target not in (target, 'both'): continue
        publish = ARTIFACTS / ('publish-macos' if target == 'mac' else 'publish-windows')
        command = [str(dotnet), 'publish', 'src/OfflinePDFConverter/OfflinePDFConverter.csproj', '-c', 'Release',
                   '-p:PaddleOcrEdition=true', '-r', rid, '--self-contained', 'true',
                   '-p:PublishSingleFile=true', '-p:IncludeNativeLibrariesForSelfExtract=true',
                   '-p:PublishTrimmed=false', '-p:NuGetAudit=false', '-p:UsedAvaloniaProducts=', '-p:DebugType=none', '-p:DebugSymbols=false', '-o', str(publish)]
        if target == 'windows': command.append('-p:PublishProfile=WindowsSingleFile')
        subprocess.run(command, cwd=ROOT, env=env, check=True)
        if target == 'windows':
            subprocess.run([os.sys.executable, str(ROOT/'scripts/verify-published-exe.py'), str(publish)], cwd=ROOT, check=True)
        package = DIST / ('macos-arm64' if target == 'mac' else 'windows-x64')
        package.mkdir(parents=True, exist_ok=True)
        if target == 'mac':
            app = package / (TITLE + '.app')
            contents = app / 'Contents'
            binaries = contents / 'MacOS'; resources = contents / 'Resources'
            binaries.mkdir(parents=True, exist_ok=True); resources.mkdir(parents=True, exist_ok=True)
            for file in publish.iterdir():
                if file.is_file(): shutil.copy2(file, binaries/file.name)
            icons = ARTIFACTS/'AppIcon.iconset'; icons.mkdir(exist_ok=True)
            for pixels in [16, 32, 128, 256, 512]:
                for scale in [1, 2]:
                    suffix = '@2x' if scale == 2 else ''
                    subprocess.run(['sips','-z',str(pixels*scale),str(pixels*scale),str(ROOT/'src/OfflinePDFConverter/Assets/AppIcon.png'),
                                    '--out',str(icons/f'icon_{pixels}x{pixels}{suffix}.png')],check=True,stdout=subprocess.DEVNULL)
            subprocess.run(['iconutil','-c','icns',str(icons),'-o',str(resources/'AppIcon.icns')],check=True)
            with (contents/'Info.plist').open('wb') as stream:
                plistlib.dump({'CFBundleName':TITLE,'CFBundleDisplayName':TITLE,'CFBundleIdentifier':'com.offlinepdfconverter.paddle',
                               'CFBundleExecutable':'OfflinePDFConverter.PaddleEdition','CFBundlePackageType':'APPL',
                               'CFBundleShortVersionString':'4.1.0','CFBundleVersion':'4.1.0','CFBundleIconFile':'AppIcon',
                               'NSHighResolutionCapable':True,'LSMinimumSystemVersion':'11.0'},stream)
            subprocess.run(['codesign','--force','--deep','--sign','-',str(app)],check=True)
            subprocess.run(['codesign','--verify','--deep','--strict',str(app)],check=True)
        else:
            files = list(publish.iterdir())
            if len(files) != 1 or files[0].suffix != '.exe': raise ValueError('Expected one Windows executable')
            shutil.copy2(files[0], package/(TITLE+'.exe'))
        documents = [ROOT/'README.md', ROOT/'THIRD_PARTY_LICENSES.md'] + [ROOT/'docs'/name for name in
                     ['MANUAL.md', 'PADDLE_EDITION.md', 'SEARCHABLE_PDF.md', 'RELEASE_DETAILS_v4.1.0.md']]
        for source in documents:
            shutil.copy2(source, package/source.name)
        archive = DIST/(TITLE+'-'+('macOS-arm64' if target == 'mac' else 'Windows-x64')+'.zip')
        deliverable = package / (TITLE + ('.app' if target == 'mac' else '.exe'))
        files = list(deliverable.rglob('*')) if target == 'mac' else [deliverable]
        files += [package/source.name for source in documents]
        with zipfile.ZipFile(archive,'w',zipfile.ZIP_DEFLATED,compresslevel=6) as bundle:
            for file in sorted(files):
                if file.is_file(): bundle.write(file,file.relative_to(package))
        digest = hashlib.sha256(archive.read_bytes()).hexdigest()
        archive.with_suffix('.zip.sha256.txt').write_text(digest+'  '+archive.name+'\n')
        print(json.dumps({'package':str(archive),'bytes':archive.stat().st_size,'sha256':digest}),flush=True)

if __name__ == '__main__': main()
