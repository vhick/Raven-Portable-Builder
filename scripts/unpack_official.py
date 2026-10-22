"""Extract a release ZIP safely; identify Raven.exe without assuming its ZIP nesting."""
from pathlib import Path
import stat
import sys
import zipfile

MAX_UNPACKED=2_000_000_000
MAX_ENTRIES=12000

def unpack(archive, destination):
    archive, destination=Path(archive),Path(destination)
    destination.mkdir(parents=True,exist_ok=True)
    total=0
    with zipfile.ZipFile(archive) as z:
        entries=z.infolist()
        if len(entries)>MAX_ENTRIES: raise ValueError('Too many archive entries')
        for info in entries:
            name=info.filename.replace('\\','/')
            bits=name.split('/')
            if (not name or name.startswith('/') or ':' in name or
                    any(b in ('..','.') for b in bits) or not name.strip()):
                raise ValueError('Unsafe archive path: '+name)
            if (info.external_attr >> 16) & 0o170000 == stat.S_IFLNK:
                raise ValueError('Symlink entry rejected: '+name)
            target=destination.joinpath(*bits)
            if not target.resolve().is_relative_to(destination.resolve()):
                raise ValueError('Archive traversal detected')
            if info.is_dir():
                target.mkdir(parents=True,exist_ok=True)
                continue
            total+=info.file_size
            if total>MAX_UNPACKED: raise ValueError('Archive is unexpectedly large')
            target.parent.mkdir(parents=True,exist_ok=True)
            with z.open(info) as source, target.open('wb') as sink:
                import shutil
                shutil.copyfileobj(source,sink,1024*1024)
    binaries=[p for p in destination.rglob('*') if p.is_file() and p.name.lower()=='raven.exe']
    # Self-contained app may be inside a single version/arch folder.
    if len(binaries)!=1: raise ValueError(f'Expected exactly one Raven.exe, got {len(binaries)}')
    source=binaries[0].parent
    files=list(source.rglob('*'))
    if len(files)<3: raise ValueError('Raven ZIP missing application dependencies')
    (destination.parent/'source_path.txt').write_text(str(source.resolve()),encoding='utf-8')
    print(f'Official Raven program folder: {source} ({len(files)} entries)')
    return source

if __name__=='__main__':
    unpack(sys.argv[1],sys.argv[2])
