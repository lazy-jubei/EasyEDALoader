#!/usr/bin/env python3
"""Deploy a locally built extension into an Altium Wine prefix, with backups."""
import argparse
import datetime
import os
from pathlib import Path
import shutil
import tempfile
import uuid
import xml.etree.ElementTree as ET

HRID = 'EasyEDA-Loader'
REQUIRED = ('EasyEDA-Loader.dll', 'EasyEDA-Loader.Ins', 'EasyEDA-Loader.rcs', 'EasyEDA-Loader.deps.json', 'Newtonsoft.Json.dll')

def windows_path(path, prefix):
    path, drive = Path(path).resolve(), (Path(prefix) / 'drive_c').resolve()
    try:
        return 'C:\\' + str(path.relative_to(drive)).replace('/', '\\')
    except ValueError:
        return 'Z:' + str(path).replace('/', '\\')

def find_extensions(prefix, explicit=None):
    if explicit:
        root = Path(explicit).resolve()
    else:
        candidates = list((Path(prefix) / 'drive_c/ProgramData/Altium').glob('Altium Designer {*/Extensions/ExtensionsRegistry.xml'))
        if len(candidates) != 1:
            raise ValueError('Specify --extensions-root when zero or multiple Altium installations are found.')
        root = candidates[0].parent
    if not (root / 'ExtensionsRegistry.xml').is_file():
        raise ValueError('No ExtensionsRegistry.xml in the selected extension folder.')
    return root

def deploy(dist, prefix, extensions_root=None, dry_run=False):
    dist = Path(dist).resolve()
    for name in REQUIRED:
        if not (dist / name).is_file():
            raise ValueError(f'Missing built artifact: {name}')
    files = [p for p in dist.iterdir() if p.is_file()]
    if any(p.name.startswith(('Altium.', 'DevExpress.')) for p in files):
        raise ValueError('The dist folder must not contain Altium SDK or DevExpress assemblies.')
    root = find_extensions(prefix, extensions_root)
    registry = root / 'ExtensionsRegistry.xml'
    tree = ET.parse(registry)
    if tree.getroot().tag != 'Extensions':
        raise ValueError('Invalid extension registry root.')
    existing = tree.getroot().findall(f"Item[@HRID='{HRID}']")
    if len(existing) > 1:
        raise ValueError('Duplicate EasyEDA entries in the registry; resolve them before deployment.')
    target = root / HRID
    if dry_run:
        return target, None
    stamp = uuid.uuid4().hex
    backup = root / f'{HRID}.{stamp}.backup'
    backup.mkdir()
    shutil.copy2(registry, backup / registry.name)
    if target.exists():
        shutil.copytree(target, backup / HRID)
    stage = Path(tempfile.mkdtemp(prefix=f'.{HRID}-', dir=root))
    registry_temp = root / f'.ExtensionsRegistry.{stamp}.tmp'
    try:
        for file in files:
            shutil.copy2(file, stage / file.name)
        if existing:
            item = existing[0]
        else:
            item = ET.SubElement(tree.getroot(), 'Item', HRID=HRID, Guid='8035C261-E5FE-403B-A9B5-9ABFFB6E0EF5')
            today = (datetime.datetime.now() - datetime.datetime(1899, 12, 30)).total_seconds() / 86400
            fields = {'Status': '0', 'VaultGuid': '', 'CreatedBy': 'EasyEDALoader contributors',
                      'CategoryGuid': '793A1F67-0B22-4E01-A5DE-3176A1E8C60D', 'CategoryName': '',
                      'ReadMe': '', 'Help': '', 'Requirements': '', 'Title': HRID,
                      'ShortDescription': HRID, 'LongDescription': 'Import EasyEDA components',
                      'SmallImage': '', 'LargeImage': '', 'VersionGuid': '7042BC82-F870-462D-86AF-B158AC75C490',
                      'ReleasedDate': f'{today:.7f}', 'ReleaseNotes': '', 'DateInstalled': f'{today:.7f}'}
            for name, value in fields.items():
                ET.SubElement(item, name).text = value
            pv = ET.SubElement(item, 'PlatformVersions')
            for name, version in [('DXP', '1.0.16.41'), ('EDP', '1.0.16.41'), ('MaxDXP', '0.0.0.0'), ('MaxEDP', '0.0.0.0')]:
                ET.SubElement(pv, name, BuildNumber=version)
        for name, value in [('Path', windows_path(target, prefix)), ('Version', '1.1.0.0')]:
            child = item.find(name)
            if child is None:
                child = ET.SubElement(item, name)
            child.text = value
        ET.indent(tree, space='  ')
        tree.write(registry_temp, encoding='utf-8', xml_declaration=True)
        if target.exists():
            shutil.rmtree(target)
        stage.rename(target)
        os.replace(registry_temp, registry)
    except Exception:
        if target.exists():
            shutil.rmtree(target)
        if (backup / HRID).exists():
            shutil.copytree(backup / HRID, target)
        shutil.copy2(backup / registry.name, registry)
        raise
    finally:
        if stage.exists():
            shutil.rmtree(stage)
        registry_temp.unlink(missing_ok=True)
    return target, backup

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--prefix', type=Path, default=Path.home() / 'AltiumWine/prefix')
    parser.add_argument('--dist', type=Path, default=Path(__file__).resolve().parents[1] / 'dist')
    parser.add_argument('--extensions-root', type=Path)
    parser.add_argument('--dry-run', action='store_true')
    args = parser.parse_args()
    target, backup = deploy(args.dist, args.prefix, args.extensions_root, args.dry_run)
    print(('Would install into: ' if args.dry_run else 'Installed into: ') + str(target))
    if backup:
        print('Backup: ' + str(backup))
        print('Restart Altium to load the extension.')

if __name__ == '__main__':
    main()
