"""Download ONLY an upstream Raven Windows x64 self-contained release ZIP.
The GitHub API provides version, provenance and (when present) a sha256 digest.
"""
import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import urllib.request

API = 'https://api.github.com/repos/mjishnu/Raven/releases/'


def select_asset(release):
    matches = []
    for asset in release.get('assets', []):
        name = asset.get('name', '')
        n = name.lower()
        if (n.endswith('.zip') and 'win-x64' in n and 'self-contained' in n and
                'raven' in n and 'certificate' not in n and 'cert' not in n):
            matches.append(asset)
    if len(matches) != 1:
        raise ValueError(f'Expected exactly one win-x64 self-contained Raven ZIP; found: {[a.get("name") for a in matches]}')
    return matches[0]


def version_endpoint(tag):
    if tag == 'latest':
        return API + 'latest'
    if not re.fullmatch(r'v\d+\.\d+\.\d+\.\d+', tag):
        raise ValueError('Version must be latest or an official tag like v1.1.1.0')
    return API + 'tags/' + tag


def download_asset(version, dest):
    headers = {'Accept':'application/vnd.github+json', 'User-Agent':'RavenPortableBuilder/1.0'}
    token = os.environ.get('GITHUB_TOKEN')
    if token: headers['Authorization']='Bearer ' + token
    req = urllib.request.Request(version_endpoint(version), headers=headers)
    with urllib.request.urlopen(req, timeout=60) as response:
        release = json.load(response)
    if release.get('draft') or release.get('prerelease'):
        raise ValueError('Only stable published upstream releases are supported')
    tag = release['tag_name']
    asset = select_asset(release)
    url = asset.get('browser_download_url', '')
    if not url.startswith('https://github.com/mjishnu/Raven/releases/download/'):
        raise ValueError('Unexpected upstream download URL')
    nbytes = asset.get('size', 0)
    if not (1_000_000 < nbytes < 1_500_000_000):
        raise ValueError('Unexpected release asset size: ' + str(nbytes))
    dest = Path(dest)
    dest.parent.mkdir(parents=True, exist_ok=True)
    temp = dest.with_suffix('.part')
    h=hashlib.sha256()
    total=0
    try:
        req=urllib.request.Request(url, headers={'User-Agent':'RavenPortableBuilder/1.0'})
        with urllib.request.urlopen(req,timeout=90) as r, temp.open('wb') as f:
            while True:
                block=r.read(1024*1024)
                if not block: break
                total += len(block)
                if total > nbytes or total > 1_500_000_000: raise ValueError('Download exceeds expected size')
                h.update(block); f.write(block)
        if total != nbytes: raise ValueError(f'Incomplete upstream asset: {total}/{nbytes} bytes')
        digest = asset.get('digest') or ''
        if digest.startswith('sha256:') and h.hexdigest().lower()!=digest[7:].lower():
            raise ValueError('GitHub release asset SHA256 mismatch')
        if temp.open('rb').read(2) != b'PK': raise ValueError('Downloaded file is not ZIP')
        temp.replace(dest)
    finally:
        if temp.exists(): temp.unlink()
    # Apache-2.0 redistribution requires the upstream license in every application ZIP.
    license_url = 'https://raw.githubusercontent.com/mjishnu/Raven/' + tag + '/LICENSE'
    with urllib.request.urlopen(urllib.request.Request(license_url,headers={'User-Agent':'RavenPortableBuilder/1.0'}),timeout=40) as response:
        license_text=response.read(100_000).decode('utf-8-sig')
    if 'Apache License' not in license_text or 'Version 2.0' not in license_text:
        raise ValueError('Upstream Apache-2.0 license could not be verified')
    (dest.parent/'UPSTREAM-LICENSE.txt').write_text(license_text,encoding='utf-8')
    out={'tag':tag, 'asset':asset['name'], 'sha256':h.hexdigest(), 'bytes':total, 'source_url':url}
    (dest.parent/'source.json').write_text(json.dumps(out,indent=2)+'\n',encoding='utf-8')
    print(json.dumps(out,indent=2))
    return out

if __name__=='__main__':
    ap=argparse.ArgumentParser()
    ap.add_argument('--version',default='latest')
    ap.add_argument('--output',default='work/download/RavenOfficial.zip')
    a=ap.parse_args()
    download_asset(a.version,a.output)
