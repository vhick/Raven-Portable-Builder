from pathlib import Path
import sys
import zipfile


def verify(zip_path):
    with zipfile.ZipFile(zip_path) as z:
        names=set(z.namelist())
        expected={
            'RavenPortable/RavenPortable.exe',
            'RavenPortable/App/Raven/Raven.exe',
            'RavenPortable/App/AppInfo/appinfo.ini',
            'RavenPortable/App/AppInfo/appicon.ico',
            'RavenPortable/App/AppInfo/appicon_16.png',
            'RavenPortable/App/AppInfo/appicon_32.png',
            'RavenPortable/App/AppInfo/appicon_128.png',
            'RavenPortable/Other/Manage-RavenJunction.ps1',
            'RavenPortable/Other/RAVEN-APACHE-2-LICENSE.txt',
            'RavenPortable/Data/LocalAppData/Raven/.keep',
            'RavenPortable/Data/TraceGuard/.keep',
        }
        missing=expected-names
        if missing: raise ValueError(f'Missing package entries: {missing}')
        if any(not x.startswith('RavenPortable/') for x in names): raise ValueError('Extra top-level folders in artifact')
        if any(x.lower().endswith(('.paf.exe','.msix','.msi')) for x in names): raise ValueError('Unexpected installer in artifact')
        if not z.testzip() is None: raise ValueError('ZIP CRC error')
        data=z.read('RavenPortable/App/AppInfo/appinfo.ini').decode('utf-8')
        if 'Start=RavenPortable.exe' not in data: raise ValueError('PortableApps launcher metadata invalid')
    print('PASS: PortableApps ZIP layout, original icon, portable data and launcher.')

if __name__=='__main__':
    found=list(Path('dist').glob('*.zip'))
    if len(found)!=1: raise ValueError('Expected exactly one final ZIP')
    verify(found[0])
