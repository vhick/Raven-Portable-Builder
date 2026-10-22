// Raven Registry TraceGuard v1.0. MIT License.
// Snapshot HKCU\Software\Raven + audit-only Windows integration keys.
// Remove only NEW, unmodified, explicitly portable-path-linked REG_SZ values
// within HKCU\Software\Raven, and newly created EMPTY keys there.
// NEVER touch HKLM, associations, default apps, pre-existing entries or other apps.
using System;
using System.Collections.Generic;
using System.IO;
using System.Diagnostics;
using System.Text;
using Microsoft.Win32;

internal sealed class RegistryTraceGuard
{
    private readonly string scope;
    private readonly string appPath;
    private readonly string log;
    private readonly bool test;
    private readonly Dictionary<string, Dictionary<string, string>> oldKeys;
    private bool snapshotOk;
    private readonly List<string> messages = new List<string>();
    private const int MaxKeys = 250;
    private const int MaxDepth = 5;

    internal RegistryTraceGuard(string exe, string logFile)
        : this(@"Software\Raven", exe, logFile, false) { }

    private RegistryTraceGuard(string watchedKey, string exe, string logFile, bool testing)
    {
        scope = watchedKey;
        appPath = Path.GetFullPath(exe);
        log = logFile;
        test = testing;
        oldKeys = Snapshot(out snapshotOk);
        Note("Registry TraceGuard: HKCU\\" + scope + " safe cleanup; Windows associations report-only");
        if (!snapshotOk) Note("SKIP: initial snapshot incomplete");
        if (!test) AuditSharedKeys("before");
    }

    private static string Signature(RegistryKey key, string name)
    {
        RegistryValueKind kind = key.GetValueKind(name);
        object value = key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
        byte[] bytes = value as byte[];
        string[] items = value as string[];
        string txt = bytes != null ? Convert.ToBase64String(bytes) : (items != null ? string.Join("\0", items) : (value == null ? "<null>" : value.ToString()));
        return kind + ":" + txt;
    }

    private Dictionary<string, Dictionary<string, string>> Snapshot(out bool ok)
    {
        Dictionary<string, Dictionary<string, string>> found = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        ok = true;
        try
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(scope, false))
                if (key != null) Walk(key, scope, 0, found);
        }
        catch (Exception ex) { ok = false; Note("SKIP registry snapshot failed: " + ex.GetType().Name); }
        return found;
    }

    private static void Walk(RegistryKey key, string path, int depth,
        Dictionary<string, Dictionary<string, string>> found)
    {
        if (depth > MaxDepth || found.Count >= MaxKeys) throw new IOException("Registry scan limit exceeded");
        Dictionary<string, string> values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string name in key.GetValueNames()) values[name] = Signature(key, name);
        found[path] = values;
        if (depth == MaxDepth)
        {
            if (key.GetSubKeyNames().Length != 0) throw new IOException("Registry depth limit exceeded");
            return;
        }
        foreach (string child in key.GetSubKeyNames())
        {
            using (RegistryKey subkey = key.OpenSubKey(child, false))
            {
                if (subkey == null) throw new IOException("Registry changed mid-snapshot");
                Walk(subkey, path + "\\" + child, depth + 1, found);
            }
        }
    }

    private bool IsLinked(RegistryKey key, string name)
    {
        RegistryValueKind kind = key.GetValueKind(name);
        if (kind != RegistryValueKind.String && kind != RegistryValueKind.ExpandString) return false;
        string value = key.GetValue(name, "", RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
        // Exact full executable path substring. Never match generic words like 'Raven'.
        return value != null && value.IndexOf(appPath, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private void AuditSharedKeys(string when)
    {
        string[] watched = new string[] {
            @"Software\Microsoft\Windows\CurrentVersion\App Paths\Raven.exe",
            @"Software\Classes\Applications\Raven.exe",
            @"Software\Classes\.msix\OpenWithProgids",
            @"Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\.msix\OpenWithList"
        };
        foreach (string item in watched)
        {
            try
            {
                using (RegistryKey reg = Registry.CurrentUser.OpenSubKey(item, false))
                    Note("AUDIT ONLY " + when + ": HKCU\\" + item + " => " + (reg == null ? "absent" : "present"));
            }
            catch (Exception e) { Note("SKIP shared registry audit: " + e.GetType().Name); }
        }
    }

    private static bool RavenStillRunning()
    {
        try
        {
            Process[] processes = Process.GetProcessesByName("Raven");
            bool found = processes.Length != 0;
            foreach (Process p in processes) p.Dispose();
            return found;
        }
        catch { return true; }
    }

    internal void Cleanup()
    {
        try
        {
            if (!test && RavenStillRunning())
            {
                Note("SKIP all registry cleanup because a Raven process is still running");
                return;
            }
            bool complete;
            Dictionary<string, Dictionary<string, string>> now = Snapshot(out complete);
            if (!snapshotOk || !complete)
            {
                Note("SKIP registry cleanup because snapshot was incomplete");
                return;
            }
            foreach (KeyValuePair<string, Dictionary<string, string>> kv in now)
            {
                Dictionary<string, string> old;
                if (!oldKeys.TryGetValue(kv.Key, out old))
                    old = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (KeyValuePair<string, string> v in kv.Value)
                {
                    if (old.ContainsKey(v.Key))
                    {
                        if (old[v.Key] != v.Value) Note("PRESERVE pre-existing changed registry value: " + kv.Key + "\\" + v.Key);
                        continue;
                    }
                    Note("DETECTED NEW HKCU value: " + kv.Key + "\\" + v.Key);
                    try
                    {
                        using (RegistryKey key = Registry.CurrentUser.OpenSubKey(kv.Key, true))
                        {
                            if (key == null || !Array.Exists(key.GetValueNames(), delegate(string n) { return string.Equals(n, v.Key, StringComparison.OrdinalIgnoreCase); })) continue;
                            if (Signature(key, v.Key) != v.Value || !IsLinked(key, v.Key))
                            { Note("PRESERVE unlinked or modified value: " + kv.Key + "\\" + v.Key); continue; }
                            key.DeleteValue(v.Key, false);
                            Note("REMOVED new path-linked Raven value: " + kv.Key + "\\" + v.Key);
                        }
                    }
                    catch (Exception e) { Note("SKIP deleting value: " + e.GetType().Name); }
                }
            }
            List<string> added = new List<string>();
            foreach (string key in now.Keys) if (!oldKeys.ContainsKey(key)) added.Add(key);
            added.Sort(delegate(string a, string b) { return b.Length.CompareTo(a.Length); });
            foreach (string key in added)
            {
                try
                {
                    int idx = key.LastIndexOf('\\');
                    if (idx < 0) continue;
                    string parent = key.Substring(0, idx);
                    string child = key.Substring(idx + 1);
                    using (RegistryKey container = Registry.CurrentUser.OpenSubKey(parent, true))
                    {
                        if (container == null) continue;
                        using (RegistryKey leaf = container.OpenSubKey(child, false))
                        {
                            if (leaf == null || leaf.GetValueNames().Length != 0 || leaf.GetSubKeyNames().Length != 0) continue;
                        }
                        container.DeleteSubKey(child, false); // no recursive deletes
                        Note("REMOVED new empty Raven HKCU key: " + key);
                    }
                }
                catch (Exception e) { Note("SKIP deleting registry key: " + e.GetType().Name); }
            }
        }
        catch (Exception ex) { Note("SKIP registry cleanup: " + ex.GetType().Name); }
        finally { if (!test) AuditSharedKeys("after"); Save(); }
    }

    private void Note(string value) { if (messages.Count < 1500) messages.Add(DateTime.UtcNow.ToString("u") + " " + value); }
    private void Save()
    {
        try { Directory.CreateDirectory(Path.GetDirectoryName(log)); File.WriteAllLines(log, messages.ToArray(), Encoding.UTF8); }
        catch { }
    }

    internal static int SelfTest()
    {
        string key = @"Software\RavenRegistryTest_" + Guid.NewGuid().ToString("N");
        string app = @"C:\Portable\RavenPortable\App\Raven\Raven.exe";
        string path = Path.Combine(Path.GetTempPath(), "RavenRegistryTest_" + Guid.NewGuid().ToString("N"), "registry.log");
        try
        {
            using (RegistryKey before = Registry.CurrentUser.CreateSubKey(key + @"\Raven"))
                before.SetValue("old", "important", RegistryValueKind.String);
            RegistryTraceGuard guard = new RegistryTraceGuard(key, app, path, true);
            using (RegistryKey modified = Registry.CurrentUser.OpenSubKey(key + @"\Raven", true))
            {
                modified.SetValue("new portable", app, RegistryValueKind.String);
                modified.SetValue("other", "do not delete", RegistryValueKind.String);
            }
            using (RegistryKey empty = Registry.CurrentUser.CreateSubKey(key + @"\Raven\Empty")) { }
            guard.Cleanup();
            using (RegistryKey after = Registry.CurrentUser.OpenSubKey(key + @"\Raven", false))
            {
                if (after.GetValue("old", null) == null) return 11;
                if (after.GetValue("other", null) == null) return 12;
                if (after.GetValue("new portable", null) != null) return 13;
            }
            if (Registry.CurrentUser.OpenSubKey(key + @"\Raven\Empty", false) != null) return 14;
            if (!File.Exists(path)) return 15;
            return 0;
        }
        catch { return 99; }
        finally
        {
            try { Registry.CurrentUser.DeleteSubKeyTree(key, false); } catch { } // Self-test sandbox only.
            try { Directory.Delete(Path.GetDirectoryName(path), true); } catch { }
        }
    }
}
