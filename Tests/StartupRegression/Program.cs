using System;
using System.Reflection;
using Microsoft.Win32;
using ExplorerTabUtility.Managers;

internal static class Program
{
    private const string RunPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ApprovedPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
    private const string AppName = "ExplorerTabUtility";
    private const string ExePath = @"C:\Startup 测试\Explorer Tab Utility\ExplorerTabUtility.exe";
    private static int _failures;

    private static int Main()
    {
        Check("missing Run and StartupApproved keys are created", root =>
        {
            Toggle(root);
            Assert(Enabled(root), "Fresh registration was not enabled.");
            using var run = root.OpenSubKey(RunPath);
            using var approved = root.OpenSubKey(ApprovedPath);
            Assert(run != null && approved != null, "A required registry key was not created.");
        });
        Check("missing Run key is created", root =>
        {
            using (root.CreateSubKey(ApprovedPath)) { }
            Toggle(root);
            using var run = root.OpenSubKey(RunPath);
            Assert(run != null && Enabled(root), "Missing Run key was not created.");
        });
        Check("missing StartupApproved key is created", root =>
        {
            using (root.CreateSubKey(RunPath)) { }
            Toggle(root);
            using var approved = root.OpenSubKey(ApprovedPath);
            Assert(approved != null, "Missing StartupApproved key was not created.");
            var value = approved!.GetValue(AppName) as byte[];
            Assert(value != null && value.Length == 12 && value[0] == 2, "New approval is not enabled.");
        });
        Check("new command quotes a path containing spaces", root =>
        {
            using (root.CreateSubKey(RunPath)) { }
            using (root.CreateSubKey(ApprovedPath)) { }
            Toggle(root);
            using var run = root.OpenSubKey(RunPath);
            Assert((string?)run!.GetValue(AppName) == $"\"{ExePath}\"", "Startup command was not quoted.");
            Assert(run.GetValueKind(AppName) == RegistryValueKind.String, "Startup command is not REG_SZ.");
        });
        Check("legacy, quoted and case-varied paths are recognized", root =>
        {
            using var run = root.CreateSubKey(RunPath)!;
            foreach (var command in new[] { ExePath, $"\"{ExePath}\"", ExePath.ToUpperInvariant(), $"\"{ExePath.ToUpperInvariant()}\"" })
            {
                run.SetValue(AppName, command);
                Assert(Enabled(root), "Valid startup command was not recognized: " + command);
            }
        });
        Check("other executables and commands with arguments do not match", root =>
        {
            using var run = root.CreateSubKey(RunPath)!;
            foreach (var command in new[] { ExePath + ".other.exe", $"\"{ExePath}.other.exe\"", ExePath + " --background", $"\"{ExePath}\" --background", $"\"{ExePath}\"\"" })
            {
                run.SetValue(AppName, command);
                Assert(!Enabled(root), "A different startup command was accepted: " + command);
            }
        });
        Check("system-disabled entries stay disabled until explicitly enabled", root =>
        {
            using var run = root.CreateSubKey(RunPath)!;
            using var approved = root.CreateSubKey(ApprovedPath)!;
            var disabled = new byte[12];
            disabled[0] = 3;
            disabled[4] = 42;
            approved.SetValue(AppName, disabled, RegistryValueKind.Binary);
            foreach (var command in new[] { ExePath, $"\"{ExePath}\"" })
            {
                run.SetValue(AppName, command);
                Assert(!Enabled(root), "A system-disabled entry was reported enabled.");
            }
            Assert(((byte[])approved.GetValue(AppName)!)[4] == 42, "Reading startup state changed the approval record.");
            Toggle(root);
            Assert(Enabled(root) && ((byte[])approved.GetValue(AppName)!)[0] == 2, "Explicit enabling did not restore approval.");
        });
        Check("toggling existing bare and quoted entries removes them", root =>
        {
            using var run = root.CreateSubKey(RunPath)!;
            using var approved = root.CreateSubKey(ApprovedPath)!;
            foreach (var command in new[] { ExePath, $"\"{ExePath}\"" })
            {
                run.SetValue(AppName, command);
                approved.SetValue(AppName, new byte[] { 2 }, RegistryValueKind.Binary);
                Toggle(root);
                Assert(run.GetValue(AppName) == null && approved.GetValue(AppName) == null, "Toggling an enabled entry did not remove it.");
            }
        });
        Check("removing an absent entry does not create keys", root =>
        {
            Invoke("RemoveFromStartup", root);
            using var run = root.OpenSubKey(RunPath);
            using var approved = root.OpenSubKey(ApprovedPath);
            Assert(run == null && approved == null, "Removal created registry keys.");
        });
        Check("removal preserves unrelated startup entries", root =>
        {
            using var run = root.CreateSubKey(RunPath)!;
            using var approved = root.CreateSubKey(ApprovedPath)!;
            run.SetValue(AppName, ExePath);
            run.SetValue("UnrelatedApp", "unrelated.exe");
            approved.SetValue(AppName, new byte[] { 2 }, RegistryValueKind.Binary);
            approved.SetValue("UnrelatedApp", new byte[] { 3 }, RegistryValueKind.Binary);
            Invoke("RemoveFromStartup", root);
            Assert(run.GetValue(AppName) == null && approved.GetValue(AppName) == null, "Application startup values remained.");
            Assert((string?)run.GetValue("UnrelatedApp") == "unrelated.exe" && ((byte[])approved.GetValue("UnrelatedApp")!)[0] == 3, "Removal changed another application's entry.");
        });
        Check("empty executable paths do not create keys", root =>
        {
            foreach (var path in new string?[] { null, "", " " })
            {
                Invoke("ToggleStartup", root, path);
                Assert(!(bool)Invoke("IsStartupEnabledFor", root, path)!, "Empty executable path was enabled.");
            }
            Assert(root.SubKeyCount == 0, "Empty executable paths created registry keys.");
        });
        Console.WriteLine(_failures == 0 ? "PASS: 11 isolated startup registration checks." : $"FAIL: {_failures} startup registration checks.");
        return _failures == 0 ? 0 : 1;
    }

    private static bool Enabled(RegistryKey root) => (bool)Invoke("IsStartupEnabledFor", root, ExePath)!;
    private static void Toggle(RegistryKey root) => Invoke("ToggleStartup", root, ExePath);
    private static object? Invoke(string method, params object?[] arguments) =>
        typeof(RegistryManager).GetMethod(method, BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, arguments);

    private static void Check(string name, Action<RegistryKey> check)
    {
        var rootPath = @"Software\ExplorerTabUtility.StartupRegression." + Guid.NewGuid().ToString("N");
        RegistryKey? root = null;
        try
        {
            root = Registry.CurrentUser.CreateSubKey(rootPath) ?? throw new InvalidOperationException("Cannot create the isolated test registry key.");
            check(root);
            Console.WriteLine("PASS: " + name);
        }
        catch (Exception exception)
        {
            _failures++;
            Console.Error.WriteLine("FAIL: " + name + ": " + (exception.InnerException ?? exception).Message);
        }
        finally
        {
            root?.Dispose();
            Registry.CurrentUser.DeleteSubKeyTree(rootPath, false);
        }
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
