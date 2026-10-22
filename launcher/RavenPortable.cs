// Raven Portable Builder: scoped launcher and dynamic TraceGuard. MIT License.
// Upstream Raven remains unmodified. All Windows applications installed BY Raven
// are deliberate host changes and are never cleaned by this launcher.
using System;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows.Forms;

internal static class RavenPortable
{
    private static int Main(string[] args)
    {
        // Headless self-tests MUST run before executable checks or GUI dialogs.
        if (args.Length == 1 && args[0] == "--portable-self-test") return SelfTest();
        if (args.Length == 1 && args[0] == "--traceguard-self-test") return TraceGuard.SelfTest();
        if (args.Length == 1 && args[0] == "--registrytrace-self-test") return RegistryTraceGuard.SelfTest();
        if (args.Length == 1 && args[0] == "--junction-self-test") return JunctionSelfTest();
        try { return Launch(args); }
        catch (Exception e)
        {
            MessageBox.Show("Raven Portable could not start safely.\r\n\r\n" + e.Message,
                "Raven Portable", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
    }
    private static string Root { get { return Path.GetFullPath(AppDomain.CurrentDomain.BaseDirectory); } }
    private static string Script { get { return Path.Combine(Root, "Other", "Manage-RavenJunction.ps1"); } }
    private static int SelfTest()
    {
        string root = Root;
        string app = Path.GetFullPath(Path.Combine(root, "App", "Raven", "Raven.exe"));
        string data = Path.GetFullPath(Path.Combine(root, "Data", "LocalAppData", "Raven"));
        return app.StartsWith(Path.Combine(root, "App"), StringComparison.OrdinalIgnoreCase) &&
            data.StartsWith(Path.Combine(root, "Data"), StringComparison.OrdinalIgnoreCase) &&
            !data.StartsWith(Path.Combine(root, "App"), StringComparison.OrdinalIgnoreCase) ? 0 : 10;
    }
    private static int Launch(string[] args)
    {
        string root = Root;
        string binary = Path.Combine(root, "App", "Raven", "Raven.exe");
        string host = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Raven");
        string data = Path.Combine(root, "Data", "LocalAppData", "Raven");
        string temp = Path.Combine(root, "Data", "Temp");
        string trace = Path.Combine(root, "Data", "TraceGuard");
        string marker = Path.Combine(trace, "host-junction-owner.json");
        if (!File.Exists(binary)) throw new FileNotFoundException("App\\Raven\\Raven.exe is missing. Extract the COMPLETE application ZIP.");
        if (!File.Exists(Script)) throw new FileNotFoundException("Other\\Manage-RavenJunction.ps1 is missing.");
        bool acquired;
        using (Mutex guard = new Mutex(true, @"Local\RavenPortable_" + Hash(root), out acquired))
        {
            if (!acquired) throw new InvalidOperationException("This Raven Portable folder is already running.");
            try
            {
                if (AnyRaven()) throw new InvalidOperationException("Another Raven process is running. Close it before starting Raven Portable.");
                Directory.CreateDirectory(data); Directory.CreateDirectory(temp); Directory.CreateDirectory(trace);
                TraceGuard files = new TraceGuard(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    Path.Combine(trace, "last-run.log"));
                RegistryTraceGuard registry = new RegistryTraceGuard(binary, Path.Combine(trace, "registry.log"));
                // Fail safely if a normal Raven profile already exists at host. Never overwrite it.
                RunJunction("mount", host, data, marker);
                int code = 1;
                try
                {
                    ProcessStartInfo psi = new ProcessStartInfo();
                    psi.FileName = binary;
                    psi.WorkingDirectory = Path.GetDirectoryName(binary);
                    psi.UseShellExecute = false;
                    psi.EnvironmentVariables["TEMP"] = temp;
                    psi.EnvironmentVariables["TMP"] = temp;
                    // No proprietary path flags; the scoped junction handles .NET Known Folders.
                    StringBuilder forwarded = new StringBuilder();
                    foreach (string arg in args) forwarded.Append(Quote(arg)).Append(' ');
                    psi.Arguments = forwarded.ToString();
                    using (Process child = Process.Start(psi))
                    {
                        if (child == null) throw new InvalidOperationException("Raven.exe did not start.");
                        child.WaitForExit();
                        code = child.ExitCode;
                    }
                }
                finally
                {
                    // Do NOT remove the junction while Raven is active; that could corrupt downloads.
                    if (AnyRaven())
                    {
                        Log(trace, "PRESERVE junction: Raven remains running. Next launch recovers after exit/reboot.");
                    }
                    else
                    {
                        try { registry.Cleanup(); } catch (Exception e) { Log(trace, "Registry TraceGuard exception: " + e.Message); }
                        try { RunJunction("unmount", host, data, marker); }
                        catch (Exception e) { Log(trace, "UNMOUNT FAILED (conservative): " + e.Message); }
                        try { files.Cleanup(); } catch (Exception e) { Log(trace, "File TraceGuard exception: " + e.Message); }
                    }
                }
                return code;
            }
            finally { guard.ReleaseMutex(); }
        }
    }
    private static void RunJunction(string mode, string host, string target, string marker, bool silent = false)
    {
        ProcessStartInfo psi = new ProcessStartInfo();
        psi.FileName = "powershell.exe";
        psi.WorkingDirectory = Root;
        psi.UseShellExecute = false; psi.CreateNoWindow = true;
        psi.RedirectStandardOutput = true; psi.RedirectStandardError = true;
        psi.Arguments = "-NoProfile -NonInteractive -ExecutionPolicy Bypass -File " + Quote(Script)
            + " -Mode " + mode + " -HostPath " + Quote(host)
            + " -TargetPath " + Quote(target) + " -MarkerPath " + Quote(marker);
        using (Process process = Process.Start(psi))
        {
            if (process == null) throw new InvalidOperationException("Windows PowerShell did not start.");
            if (!process.WaitForExit(15000)) { try { process.Kill(); } catch { } throw new TimeoutException("Junction operation exceeded 15 seconds."); }
            string output = process.StandardOutput.ReadToEnd();
            string errors = process.StandardError.ReadToEnd();
            if (process.ExitCode != 0) throw new IOException("Junction " + mode + " failed: " + errors + " " + output);
            string traceDir = Path.Combine(Root, "Data", "TraceGuard");
            if (!silent) Log(traceDir, mode.ToUpperInvariant() + ": " + output.Trim());
        }
    }
    private static bool AnyRaven()
    {
        try
        {
            string[] names = { "Raven", "Raven.Updater", "RavenUpdater" };
            bool any = false;
            foreach (string name in names)
            {
                Process[] processes = Process.GetProcessesByName(name);
                any |= processes.Length > 0;
                foreach (Process p in processes) p.Dispose();
            }
            return any;
        }
        catch { return true; }
    }
    private static void Log(string dir, string line)
    {
        try { Directory.CreateDirectory(dir); File.AppendAllText(Path.Combine(dir,"junction.log"), DateTime.UtcNow.ToString("u") + " " + line + Environment.NewLine); }
        catch { }
    }
    private static string Hash(string s)
    {
        using (SHA256 sha = SHA256.Create())
        {
            byte[] b = sha.ComputeHash(Encoding.UTF8.GetBytes(s.ToUpperInvariant()));
            StringBuilder t = new StringBuilder();
            for (int i = 0; i < 10; i++) t.Append(b[i].ToString("x2"));
            return t.ToString();
        }
    }
    private static string Quote(string arg)
    {
        StringBuilder q = new StringBuilder("\""); int backslashes = 0;
        foreach (char c in arg)
        {
            if (c == '\\') { backslashes++; continue; }
            if (c == '"') { q.Append('\\', backslashes * 2 + 1).Append('"'); backslashes = 0; continue; }
            q.Append('\\', backslashes); backslashes = 0; q.Append(c);
        }
        q.Append('\\', backslashes * 2).Append('"');
        return q.ToString();
    }
    private static int JunctionSelfTest()
    {
        string parent = Path.Combine(Path.GetTempPath(), "RavenJunctionTest_" + Guid.NewGuid().ToString("N"));
        string host = Path.Combine(parent, "Host", "Raven");
        string data = Path.Combine(parent, "PortableData", "Raven");
        string marker = Path.Combine(parent, "Trace", "owner.json");
        string phase = "initialize";
        try
        {
            if (!File.Exists(Script)) throw new FileNotFoundException("The junction test helper is not beside the launcher: " + Script);
            Directory.CreateDirectory(Path.GetDirectoryName(host));
            Directory.CreateDirectory(data);
            File.WriteAllText(Path.Combine(data,"private.txt"), "preserve");
            phase = "mount temporary test junction";
            RunJunction("mount", host, data, marker, true);
            phase = "verify temporary test junction";
            if (!Directory.Exists(host) || (File.GetAttributes(host) & FileAttributes.ReparsePoint) == 0) return 11;
            if (!File.Exists(Path.Combine(host,"private.txt"))) return 12;
            phase = "unmount temporary test junction";
            RunJunction("unmount", host, data, marker, true);
            phase = "verify portable data preserved";
            if (Directory.Exists(host)) return 13;
            if (!File.Exists(Path.Combine(data,"private.txt"))) return 14;
            // Pre-existing ordinary folder must not be overwritten.
            phase = "verify existing host folder protected";
            Directory.CreateDirectory(host);
            File.WriteAllText(Path.Combine(host,"host.txt"), "do not touch");
            bool refused = false;
            try { RunJunction("mount", host, data, marker, true); } catch { refused = true; }
            if (!refused || !File.Exists(Path.Combine(host,"host.txt"))) return 15;
            return 0;
        }
        catch (Exception ex)
        {
            // GitHub compiles as winexe. Write diagnostics to a build-local file,
            // which the workflow prints on failure. Never hide the real exception again.
            try { File.WriteAllText(Path.Combine(Root,"junction-self-test.log"),
                "Failed phase: " + phase + Environment.NewLine + ex.ToString()); } catch { }
            return 99;
        }
        finally { try { Directory.Delete(parent, true); } catch { } }
    }
}
