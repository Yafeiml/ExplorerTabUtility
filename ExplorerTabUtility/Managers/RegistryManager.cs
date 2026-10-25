using System;
using Microsoft.Win32;
using ExplorerTabUtility.Helpers;

namespace ExplorerTabUtility.Managers;

public static class RegistryManager
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string StartupApprovedKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
    private const string ExplorerAdvancedKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";
    private static readonly string? ExecutablePath = Helper.GetExecutablePath();
    public static bool IsStartupEnabled => IsStartupEnabledFor(Registry.CurrentUser, ExecutablePath);

    public static void ToggleStartup() => ToggleStartup(Registry.CurrentUser, ExecutablePath);

    private static bool IsStartupEnabledFor(RegistryKey root, string? executablePath) =>
        IsInStartup(root, executablePath) && IsStartupApprovedEnabled(root);

    private static void ToggleStartup(RegistryKey root, string? executablePath)
    {
        if (IsStartupEnabledFor(root, executablePath))
            RemoveFromStartup(root);
        else
            AddToStartup(root, executablePath);
    }

    private static bool IsInStartup(RegistryKey root, string? executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath)) return false;

        // Check if the application exists in the Run registry key and has the correct executable location
        using var key = root.OpenSubKey(RunKeyPath, false);
        var value = key?.GetValue(Constants.AppName) as string;
        return string.Equals(value, executablePath, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, $"\"{executablePath}\"", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsStartupApprovedEnabled(RegistryKey root)
    {
        using var key = root.OpenSubKey(StartupApprovedKeyPath, false);
        var value = key?.GetValue(Constants.AppName) as byte[];
        // Check first byte parity (even = enabled, odd = disabled), null and empty also mean enabled.
        return value == null || value.Length == 0 || value[0] % 2 == 0;
    }

    private static void AddToStartup(RegistryKey root, string? executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath)) return;

        // Add to Run registry key
        using var runKey = root.CreateSubKey(RunKeyPath, true) ??
                           throw new InvalidOperationException("Could not create the startup registry key.");
        runKey.SetValue(Constants.AppName, $"\"{executablePath}\"", RegistryValueKind.String);

        // Create enabled entry in StartupApproved
        var enabledData = new byte[12];
        enabledData[0] = 0x02; // Even value for enabled

        using var approvedKey = root.CreateSubKey(StartupApprovedKeyPath, true) ??
                                throw new InvalidOperationException("Could not create the startup approval registry key.");
        approvedKey.SetValue(Constants.AppName, enabledData, RegistryValueKind.Binary);
    }

    private static void RemoveFromStartup(RegistryKey root)
    {
        // Remove from Run registry key
        using var runKey = root.OpenSubKey(RunKeyPath, true);
        runKey?.DeleteValue(Constants.AppName, false);

        // Remove from StartupApproved
        using var approvedKey = root.OpenSubKey(StartupApprovedKeyPath, true);
        approvedKey?.DeleteValue(Constants.AppName, false);
    }

    public static int GetDefaultExplorerLaunchId()
    {
        using var key = OpenCurrentUserKey(ExplorerAdvancedKeyPath, false);
        if (key == null) return 1;
        return key.GetValue("LaunchTo") as int? ?? 1;
    }

    private static RegistryKey? OpenCurrentUserKey(string name, bool writable) => Registry.CurrentUser.OpenSubKey(name, writable);
}
