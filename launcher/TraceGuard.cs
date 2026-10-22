// Raven Dynamic File TraceGuard v1.0. MIT License.
// Watches ONLY application-specific AppData roots. Never delete pre-existing,
// non-empty, or reparse-point folders. Never recurse into unrelated host folders.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

internal sealed class TraceGuard
{
    private readonly string roaming;
    private readonly string local;
    private readonly string logPath;
    private readonly HashSet<string> before = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> report = new List<string>();
    private readonly bool test;
    private readonly bool initiallyRunning;
    private bool snapshotOk = true;
    private const int MaxPaths = 1500;
    private const int MaxDepth = 6;

    internal TraceGuard(string roamingDir, string localDir, string logFile) : this(roamingDir, localDir, logFile, false) { }
    private TraceGuard(string roamingDir, string localDir, string logFile, bool isTest)
    {
        roaming = roamingDir;
        local = localDir;
        logPath = logFile;
        test = isTest;
        initiallyRunning = !test && OtherRavenRunning();
        Log("TraceGuard: scoped AppData snapshot before launch");
        foreach (string root in WatchedRoots())
        {
            try { Capture(root, 0, before); }
            catch (Exception ex) { snapshotOk = false; Log("SKIP incomplete before snapshot: " + ex.GetType().Name); }
        }
    }

    private IEnumerable<string> WatchedRoots()
    {
        yield return Path.Combine(roaming, "Raven");
        yield return Path.Combine(local, "Raven");
    }

    private static bool OtherRavenRunning()
    {
        try
        {
            Process[] all = Process.GetProcessesByName("Raven");
            bool seen = all.Length > 0;
            foreach (Process p in all) p.Dispose();
            return seen;
        }
        catch { return true; }
    }

    private static bool Reparse(string path)
    {
        return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
    }

    private static void Capture(string path, int depth, HashSet<string> found)
    {
        if (!Directory.Exists(path)) return;
        if (Reparse(path)) throw new IOException("Reparse/junction detected");
        if (found.Count >= MaxPaths || depth > MaxDepth) throw new IOException("TraceGuard snapshot limit reached");
        found.Add(Path.GetFullPath(path));
        if (depth == MaxDepth) return;
        foreach (string child in Directory.EnumerateDirectories(path)) Capture(child, depth + 1, found);
    }

    internal void Cleanup()
    {
        try
        {
            Log("Post-exit: clean only NEW empty Raven AppData folders");
            if (!snapshotOk || initiallyRunning || (!test && OtherRavenRunning()))
            {
                Log("SKIP cleanup: incomplete snapshot or another Raven process running");
                return;
            }
            foreach (string root in WatchedRoots())
            {
                HashSet<string> after = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                try { Capture(root, 0, after); }
                catch (Exception ex) { Log("SKIP after-snapshot: " + ex.GetType().Name); continue; }
                List<string> created = new List<string>();
                foreach (string path in after) if (!before.Contains(path)) created.Add(path);
                // Reverse nesting: delete a newly created empty child before its new parent.
                created.Sort(delegate(string a, string b) { return b.Length.CompareTo(a.Length); });
                foreach (string dir in created)
                {
                    try
                    {
                        if (!Directory.Exists(dir) || Reparse(dir)) { Log("SKIP missing/reparse: " + dir); continue; }
                        if (Directory.GetFileSystemEntries(dir).Length != 0)
                        { Log("PRESERVE populated new directory: " + dir); continue; }
                        Directory.Delete(dir, false);
                        Log("REMOVED newly created empty directory: " + dir);
                    }
                    catch (Exception ex) { Log("SKIP protected/raced directory: " + dir + " " + ex.GetType().Name); }
                }
                foreach (string old in before)
                {
                    if (old.Equals(root, StringComparison.OrdinalIgnoreCase))
                        Log("PRESERVE pre-existing Raven directory: " + root);
                }
            }
        }
        finally { SaveLog(); }
    }

    private void Log(string line)
    {
        if (report.Count < 2000) report.Add(DateTime.UtcNow.ToString("u") + " " + line);
    }
    private void SaveLog()
    {
        try { Directory.CreateDirectory(Path.GetDirectoryName(logPath)); File.WriteAllLines(logPath, report.ToArray(), Encoding.UTF8); }
        catch { /* failed logging must never interrupt Raven */ }
    }

    internal static int SelfTest()
    {
        string sandbox = Path.Combine(Path.GetTempPath(), "RavenTraceGuardTest_" + Guid.NewGuid().ToString("N"));
        try
        {
            string roam = Path.Combine(sandbox, "Roaming");
            string loc = Path.Combine(sandbox, "Local");
            string existing = Path.Combine(roam, "Raven", "PriorFolder");
            Directory.CreateDirectory(existing);
            Directory.CreateDirectory(Path.Combine(loc, "Raven", "Before"));
            TraceGuard trace = new TraceGuard(roam, loc, Path.Combine(sandbox, "Data", "TraceGuard", "last-run.log"), true);
            Directory.CreateDirectory(Path.Combine(roam, "Raven", "Dictionaries"));
            Directory.CreateDirectory(Path.Combine(roam, "Raven", "Keep"));
            File.WriteAllText(Path.Combine(roam, "Raven", "Keep", "important.txt"), "preserve");
            Directory.CreateDirectory(Path.Combine(loc, "Raven", "New", "EmptyChild"));
            trace.Cleanup();
            if (!Directory.Exists(existing)) return 11;
            if (Directory.Exists(Path.Combine(roam, "Raven", "Dictionaries"))) return 12;
            if (!File.Exists(Path.Combine(roam, "Raven", "Keep", "important.txt"))) return 13;
            if (Directory.Exists(Path.Combine(loc, "Raven", "New"))) return 14;
            if (!Directory.Exists(Path.Combine(loc, "Raven", "Before"))) return 15;
            if (!File.Exists(Path.Combine(sandbox, "Data", "TraceGuard", "last-run.log"))) return 16;
            return 0;
        }
        catch { return 99; }
        finally { try { Directory.Delete(sandbox, true); } catch { } }
    }
}
