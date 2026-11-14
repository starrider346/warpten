import argparse
import json
from pathlib import Path
import subprocess
import zipfile

ROOT = Path(__file__).resolve().parents[1]


def main():
    parser = argparse.ArgumentParser(description="Export WarpTen's DLL, metadata and listing images as a ZIP.")
    parser.add_argument('--dll', type=Path, default=ROOT / 'WarpTen/bin/Release/netstandard2.1/WarpTen.dll')
    parser.add_argument('--output', type=Path, default=ROOT / 'Releases/WarpTen.zip')
    parser.add_argument('--build', action='store_true', help='Build Release before exporting')
    args = parser.parse_args()
    if args.build:
        subprocess.run(['dotnet', 'build', str(ROOT / 'WarpTen/WarpTen.csproj'),
                        '-c', 'Release', '-p:SD_MODS_DIR='], cwd=ROOT, check=True)
    manifest = ROOT / 'mod.json'
    if not manifest.is_file():
        parser.error('Missing input: ' + str(manifest))
    try:
        metadata = json.loads(manifest.read_text(encoding='utf-8'))
    except json.JSONDecodeError as error:
        parser.error('Invalid mod.json: ' + str(error))
    files = [(args.dll, 'Mods/WarpTen.dll'), (manifest, 'mod.json')]
    images = ([metadata['thumbnail']] if metadata.get('thumbnail') else []) + metadata.get('images', [])
    for name in dict.fromkeys(images):
        path = Path(name)
        if path.is_absolute() or '..' in path.parts:
            parser.error('Listing image must use a project-relative path: ' + name)
        files.append((ROOT / path, path.as_posix()))
    for source, _ in files:
        if not source.is_file():
            parser.error('Missing input: ' + str(source))
        if args.output.resolve() == source.resolve():
            parser.error('Output must not overwrite an input file')
    args.output.parent.mkdir(parents=True, exist_ok=True)
    with zipfile.ZipFile(args.output, 'w', zipfile.ZIP_DEFLATED) as archive:
        for source, name in files:
            archive.write(source, name)
    print('Exported:', args.output)


if __name__ == '__main__':
    main()
