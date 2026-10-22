"""Assemble one top-level RavenPortable ZIP; ZIP-only, Data preserved on upgrade."""
import json
from pathlib import Path
import shutil
import zipfile

ROOT=Path(__file__).resolve().parents[1]
WORK=ROOT/'work'
DIST=ROOT/'dist'
STAGE=WORK/'stage'/'RavenPortable'


def build(root=ROOT):
    root=Path(root)
    work=root/'work'
    stage=work/'stage'/'RavenPortable'
    source=Path((work/'source_path.txt').read_text(encoding='utf-8').strip())
    if not source.is_dir() or not any(p.name.lower() == 'raven.exe' for p in source.iterdir() if p.is_file()): raise ValueError('Missing official Raven.exe source folder')
    launcher=work/'RavenPortable.exe'
    icon=work/'appicon.ico'
    if not launcher.is_file() or not icon.is_file(): raise ValueError('Missing compiled launcher / original upstream icon')
    if stage.parent.exists(): shutil.rmtree(stage.parent)
    (stage/'App').mkdir(parents=True)
    shutil.copytree(source,stage/'App'/'Raven')
    for exe in (stage/'App'/'Raven').iterdir():
        if exe.is_file() and exe.name.lower() == 'raven.exe' and exe.name != 'Raven.exe':
            exe.rename(exe.with_name('Raven.exe'))
    shutil.copy2(launcher, stage/'RavenPortable.exe')
    info=stage/'App'/'AppInfo'
    info.mkdir()
    shutil.copy2(icon, info/'appicon.ico')
    shutil.copy2(icon, info/'appicon_1.ico')
    shutil.copy2(icon, info/'appicon_2.ico')
    for size in (16,32,128):
        shutil.copy2(work/'icons'/f'appicon_{size}.png', info/f'appicon_{size}.png')
    (info/'appinfo.ini').write_text('''[Format]
Type=PortableApps.comFormat
Version=3.8

[Details]
Name=Raven Portable
AppID=RavenPortable
Publisher=mjishnu & Portable Builder
Homepage=https://github.com/mjishnu/Raven
Category=Utilities
Description=Portable Raven Microsoft Store alternative client
Language=Multilingual

[License]
Shareable=true
OpenSource=true
Freeware=true
CommercialUse=true

[Version]
PackageVersion=1.0.0.0
DisplayVersion=Portable Builder 1.1

[Control]
Icons=1
Start=RavenPortable.exe
''',encoding='utf-8')
    for directory in ('Data/LocalAppData/Raven','Data/Temp','Data/TraceGuard'):
        (stage/directory).mkdir(parents=True,exist_ok=True)
        (stage/directory/'.keep').write_text('',encoding='utf-8')
    other=stage/'Other'
    other.mkdir()
    shutil.copy2(root/'Other'/'Manage-RavenJunction.ps1', other/'Manage-RavenJunction.ps1')
    shutil.copy2(root/'README.md',other/'BUILDER-README.txt')
    upstream_license=work/'download'/'UPSTREAM-LICENSE.txt'
    if upstream_license.exists(): shutil.copy2(upstream_license,other/'RAVEN-APACHE-2-LICENSE.txt')
    source_json=work/'download'/'source.json'
    if source_json.exists(): shutil.copy2(source_json,other/'upstream-source.json')
    dist=root/'dist'
    dist.mkdir(exist_ok=True)
    source_info=json.loads(source_json.read_text(encoding='utf-8')) if source_json.exists() else {'tag':'test'}
    output=dist/f"RavenPortable-{source_info['tag']}-Windows-x64.zip"
    with zipfile.ZipFile(output,'w',compression=zipfile.ZIP_DEFLATED,compresslevel=6,allowZip64=True) as z:
        for item in sorted(stage.rglob('*')):
            if item.is_file(): z.write(item, item.relative_to(stage.parent).as_posix())
    print('Created',output,'bytes',output.stat().st_size)
    return output

if __name__=='__main__': build()
