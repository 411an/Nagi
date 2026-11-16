namespace Nagi.Core.Helpers;

/// <summary>
///     Parses unpackaged command-line arguments. <see cref="Environment.GetCommandLineArgs" />
///     includes the host binary at index 0; <c>Main(string[] args)</c> does not.
/// </summary>
public static class CommandLineArgs
{
    public static string? TryGetFilePath(IReadOnlyList<string>? args, string? processPath = null)
    {
        if (args is null || args.Count == 0) return null;

        processPath ??= Environment.ProcessPath;

        for (var i = args.Count - 1; i >= 0; i--)
        {
            var candidate = args[i];
            if (string.IsNullOrEmpty(candidate) || candidate.StartsWith('-')) continue;
            if (i > 0 && args[i - 1].Equals(AppDataLocator.DataDirectoryArgument, StringComparison.OrdinalIgnoreCase))
                continue;
            if (IsHostBinary(candidate, processPath)) continue;

            if (File.Exists(candidate) || Directory.Exists(candidate))
                return candidate;
        }

        return null;
    }

    private static bool IsHostBinary(string path, string? processPath)
    {
        try
        {
            var full = Path.GetFullPath(path);
            if (!string.IsNullOrEmpty(processPath) &&
                string.Equals(full, Path.GetFullPath(processPath), StringComparison.OrdinalIgnoreCase))
                return true;

            var baseDir = AppContext.BaseDirectory;
            var exe = Path.GetFullPath(Path.Combine(baseDir, "Nagi.exe"));
            var dll = Path.GetFullPath(Path.Combine(baseDir, "Nagi.dll"));
            return string.Equals(full, exe, StringComparison.OrdinalIgnoreCase)
                   || string.Equals(full, dll, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }
}
