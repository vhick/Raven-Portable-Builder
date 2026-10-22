import importlib.util
import json
import pathlib
import shutil
import stat
import tempfile
import unittest
import zipfile

ROOT=pathlib.Path(__file__).resolve().parents[1]

def load(name):
    spec=importlib.util.spec_from_file_location(name,ROOT/'scripts'/f'{name}.py')
    mod=importlib.util.module_from_spec(spec)
    spec.loader.exec_module(mod)
    return mod

download=load('download_official')
unpack=load('unpack_official')
package=load('package_app')
verify=load('verify_package')

class BuilderTests(unittest.TestCase):
    def test_01_latest_tag_endpoint(self):
        self.assertEqual(download.version_endpoint('latest'),download.API+'latest')
    def test_02_official_tag_endpoint(self):
        self.assertEqual(download.version_endpoint('v1.1.1.0'),download.API+'tags/v1.1.1.0')
    def test_03_tag_injection_rejected(self):
        for bad in ('v1.1.1.0/../../evil','v1.1.1-beta','main','v1.1.1.0;whoami'):
            with self.subTest(tag=bad), self.assertRaises(ValueError): download.version_endpoint(bad)
    def test_04_select_only_x64_self_contained(self):
        release={'assets':[{'name':'raven_cert.zip'},{'name':'Raven-v1.1.1.0-win-x86-self-contained.zip'},
                           {'name':'Raven-v1.1.1.0-win-x64.zip'},{'name':'Raven-v1.1.1.0-win-x64-self-contained.zip'}]}
        self.assertEqual(download.select_asset(release)['name'],'Raven-v1.1.1.0-win-x64-self-contained.zip')
    def test_05_reject_ambiguous_assets(self):
        with self.assertRaises(ValueError): download.select_asset({'assets':[{'name':'x-win-x64-self-contained.zip'}]})
    def test_06_reject_zip_traversal(self):
        with tempfile.TemporaryDirectory() as temp:
            f=pathlib.Path(temp)/'bad.zip'
            with zipfile.ZipFile(f,'w') as z: z.writestr('../escape.exe','evil')
            with self.assertRaises(ValueError): unpack.unpack(f,pathlib.Path(temp)/'out')
    def test_07_reject_zip_symlink(self):
        with tempfile.TemporaryDirectory() as temp:
            f=pathlib.Path(temp)/'bad.zip'
            info=zipfile.ZipInfo('link')
            info.create_system=3; info.external_attr=(stat.S_IFLNK | 0o777) <<16
            with zipfile.ZipFile(f,'w') as z: z.writestr(info,'target')
            with self.assertRaises(ValueError): unpack.unpack(f,pathlib.Path(temp)/'out')
    def test_08_nested_zip_discovery(self):
        with tempfile.TemporaryDirectory() as temp:
            path=pathlib.Path(temp); f=path/'in.zip'
            with zipfile.ZipFile(f,'w') as z:
                z.writestr('v1/x64/raven.exe','mock exe')
                z.writestr('v1/x64/raven.dll','mock dll')
                z.writestr('v1/x64/raven.deps.json','{}')
                z.writestr('v1/x64/test.dat','data')
            source=unpack.unpack(f,path/'work'/'extracted')
            self.assertEqual(source.name,'x64')
            self.assertEqual((path/'work'/'source_path.txt').read_text().strip(),str(source.resolve()))
    def test_09_reject_two_raven_exes(self):
        with tempfile.TemporaryDirectory() as temp:
            path=pathlib.Path(temp); f=path/'in.zip'
            with zipfile.ZipFile(f,'w') as z:
                for folder in ('x','y'):
                    z.writestr(folder+'/raven.exe','mock')
                    z.writestr(folder+'/test.dat','data')
            with self.assertRaises(ValueError): unpack.unpack(f,path/'work'/'extracted')
    def test_10_full_simulated_package(self):
        with tempfile.TemporaryDirectory() as temp:
            r=pathlib.Path(temp)
            app=r/'work'/'source'/'somefolder'; app.mkdir(parents=True)
            (app/'raven.exe').write_bytes(b'MZ--sample')
            (app/'file1.dat').write_bytes(b'data')
            (app/'file2.dat').write_bytes(b'data2')
            (app/'file3.dat').write_bytes(b'data3')
            (r/'work'/'source_path.txt').write_text(str(app),encoding='utf-8')
            (r/'work'/'RavenPortable.exe').write_bytes(b'MZ--launcher')
            (r/'work'/'appicon.ico').write_bytes(b'\x00\x00\x01\x00')
            (r/'work'/'icons').mkdir()
            for size in (16,32,128): (r/'work'/'icons'/f'appicon_{size}.png').write_bytes(b'\x89PNG')
            (r/'Other').mkdir()
            shutil.copy2(ROOT/'Other'/'Manage-RavenJunction.ps1',r/'Other'/'Manage-RavenJunction.ps1')
            (r/'README.md').write_text('test')
            (r/'work'/'download').mkdir()
            (r/'work'/'download'/'source.json').write_text(json.dumps({'tag':'v1.1.1.0'}))
            (r/'work'/'download'/'UPSTREAM-LICENSE.txt').write_text('Apache License Version 2.0')
            output=package.build(r)
            verify.verify(output)
            with zipfile.ZipFile(output) as z:
                self.assertIn('RavenPortable/App/Raven/Raven.exe',z.namelist())
                self.assertNotIn('RavenPortable/App/Raven/raven.exe',z.namelist())
    def test_11_junction_exact_host_path_scoping(self):
        source=(ROOT/'Other'/'Manage-RavenJunction.ps1').read_text()
        for signal in ('ItemType Junction','HasValidOwner','Directory]::Delete','HostPath','TargetPath','MarkerPath'):
            self.assertIn(signal,source)
        self.assertNotIn('Delete($targetPath2)',source)
    def test_12_launcher_guardrails(self):
        source=(ROOT/'launcher'/'RavenPortable.cs').read_text()
        for signal in ('--portable-self-test','--traceguard-self-test','--registrytrace-self-test','--junction-self-test',
                       'Another Raven process','AnyRaven()','LocalApplicationData','RunJunction("unmount"'):
            self.assertIn(signal,source)
        self.assertLess(source.index('--portable-self-test'),source.index('File.Exists(binary)'))
    def test_13_registry_never_edits_hklm(self):
        source=(ROOT/'launcher'/'RegistryTraceGuard.cs').read_text()
        self.assertIn('@"Software\\Raven"',source)
        self.assertNotIn('Registry.LocalMachine.Delete',source)
        self.assertIn('PRESERVE pre-existing',source)
    def test_14_file_cleanup_requires_empty_directories(self):
        source=(ROOT/'launcher'/'TraceGuard.cs').read_text()
        self.assertIn('Directory.GetFileSystemEntries(dir).Length != 0',source)
        self.assertIn('Reparse(dir)',source)
        self.assertIn('before.Contains(path)',source)
    def test_15_workflow_manual_only(self):
        s=(ROOT/'.github'/'workflows'/'build-portable.yml').read_text()
        self.assertIn('workflow_dispatch:',s)
        self.assertNotIn('schedule:',s)
        self.assertIn('retention-days: 1',s)
        self.assertIn('upload-artifact@v6',s)
        self.assertIn('registrytrace-self-test',s)
        self.assertIn('junction-self-test',s)
    def test_16_junction_fixture_is_staged_before_test(self):
        s=(ROOT/'.github'/'workflows'/'build-portable.yml').read_text()
        self.assertIn("'work').Path 'Other'",s)
        self.assertIn("Copy-Item -LiteralPath 'Other/Manage-RavenJunction.ps1'",s)
        self.assertIn("Test-Path -LiteralPath (Join-Path $testOther 'Manage-RavenJunction.ps1')",s)
        self.assertLess(s.index("Copy-Item -LiteralPath 'Other/Manage-RavenJunction.ps1'"), s.index('foreach ($test in @('))
        src=(ROOT/'launcher'/'RavenPortable.cs').read_text()
        self.assertIn('File.Exists(Script)',src)
        self.assertIn('junction-self-test.log',src)
        self.assertIn('Failed phase:',src)
    def test_17_upstream_icon_strict(self):
        s=(ROOT/'scripts'/'extract_icon.py').read_text()
        self.assertIn('RT_GROUP_ICON',s)
        self.assertIn('RT_ICON',s)
        self.assertIn('No extractable original icon group',s)

if __name__=='__main__': unittest.main()
