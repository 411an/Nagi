using Microsoft.Win32;
using Nagi.Core.Helpers;

namespace Nagi.WinUI.Helpers;

/// <summary>
///     Registers or removes a current-user Run key for unpackaged autostart.
/// </summary>
public static class Win32StartupService
{
    public const string RunValueName = "Nagi";
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ApprovedKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";

    public static bool IsEnabled()
    {
        var exePath = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exePath)) return false;

        using var runKey = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        var value = runKey?.GetValue(RunValueName) as string;
        if (string.IsNullOrWhiteSpace(value)) return false;

        if (!ValuePointsToCurrentExecutable(value, exePath)) return false;

        using var approvedKey = Registry.CurrentUser.OpenSubKey(ApprovedKeyPath, writable: false);
        if (approvedKey?.GetValue(RunValueName) is byte[] bytes && bytes.Length > 0)
        {
            // 0x03 = disabled via Task Manager / Startup apps; 0x02 = enabled.
            if (bytes[0] == 0x03) return false;
        }

        return true;
    }

    public static void SetEnabled(bool enabled)
    {
        var exePath = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exePath)) return;

        if (enabled)
        {
            using var runKey = Registry.CurrentUser.CreateSubKey(RunKeyPath);
            runKey.SetValue(RunValueName, $"\"{exePath}\" {AppDataLocator.BuildRestartArguments("--startup")}".Trim());

            using var approvedKey = Registry.CurrentUser.CreateSubKey(ApprovedKeyPath);
            approvedKey.SetValue(RunValueName, new byte[] { 0x02, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 },
                RegistryValueKind.Binary);
        }
        else
        {
            using var runKey = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            runKey?.DeleteValue(RunValueName, throwOnMissingValue: false);

            using var approvedKey = Registry.CurrentUser.OpenSubKey(ApprovedKeyPath, writable: true);
            approvedKey?.DeleteValue(RunValueName, throwOnMissingValue: false);
        }
    }

    private static bool ValuePointsToCurrentExecutable(string runValue, string exePath)
    {
        var trimmed = runValue.Trim();
        if (trimmed.StartsWith('"'))
        {
            var end = trimmed.IndexOf('"', 1);
            if (end > 1) trimmed = trimmed[1..end];
        }
        else
        {
            var space = trimmed.IndexOf(" --", StringComparison.Ordinal);
            if (space > 0) trimmed = trimmed[..space];
        }

        return string.Equals(Path.GetFullPath(trimmed), Path.GetFullPath(exePath), StringComparison.OrdinalIgnoreCase);
    }
}
