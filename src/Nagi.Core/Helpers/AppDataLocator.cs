using System.Text.Json;

namespace Nagi.Core.Helpers;

/// <summary>
///     Resolves the portable data root once per process. Call
///     <see cref="Resolve" /> from the entry point before any other data access.
/// </summary>
public static class AppDataLocator
{
    public const string DefaultDirectoryName = "datadir";
    public const string PortableConfigFileName = "portable.json";
    public const string DataDirectoryArgument = "--datadir";

    private static readonly object Gate = new();
    private static AppDataLocation? _resolved;

    /// <summary>
    ///     The resolved data root. Throws if <see cref="Resolve" /> has not been called.
    /// </summary>
    public static string Root =>
        (_resolved ?? throw new InvalidOperationException("AppDataLocator.Resolve must be called before reading Root."))
        .Root;

    public static bool IsResolved => _resolved is not null;

    public static AppDataLocation Location =>
        _resolved ?? throw new InvalidOperationException("AppDataLocator.Resolve must be called first.");

    /// <summary>
    ///     Chooses and creates the data directory. Subsequent calls return the first result.
    /// </summary>
    public static AppDataLocation Resolve(
        string baseDirectory,
        IReadOnlyList<string> args,
        Func<string, bool>? canWrite = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseDirectory);
        args ??= Array.Empty<string>();
        canWrite ??= TryEnsureWritable;

        lock (Gate)
        {
            if (_resolved is not null) return _resolved;

            if (TryGetDatadirArgument(args, out var fromArg))
            {
                _resolved = Create(ResolvePath(baseDirectory, fromArg), "arg", canWrite);
                return _resolved;
            }

            if (TryReadPortableConfig(baseDirectory, out var fromFile))
            {
                _resolved = Create(ResolvePath(baseDirectory, fromFile), "portable.json", canWrite);
                return _resolved;
            }

            var defaultRoot = Path.GetFullPath(Path.Combine(baseDirectory, DefaultDirectoryName));
            _resolved = Create(defaultRoot, "default", canWrite);
            return _resolved;
        }
    }

    /// <summary>
    ///     Test-only: clears the cached root so a new Resolve can run.
    /// </summary>
    public static void Reset()
    {
        lock (Gate)
        {
            _resolved = null;
        }
    }

    public static bool TryGetDatadirArgument(IReadOnlyList<string> args, out string path)
    {
        path = string.Empty;
        for (var i = 0; i < args.Count; i++)
        {
            var arg = args[i];
            if (arg.Equals(DataDirectoryArgument, StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 >= args.Count || string.IsNullOrWhiteSpace(args[i + 1]))
                    throw new AppDataLocatorException("The --datadir argument requires a path.");
                path = args[i + 1];
                return true;
            }

            const string prefix = "--datadir=";
            if (arg.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                path = arg[prefix.Length..];
                if (string.IsNullOrWhiteSpace(path))
                    throw new AppDataLocatorException("The --datadir argument requires a path.");
                return true;
            }
        }

        return false;
    }

    /// <summary>
    ///     Arguments to pass to <c>AppInstance.Restart</c> / autostart so a <c>--datadir</c>
    ///     override survives process restart.
    /// </summary>
    public static string BuildRestartArguments(params string[] extraFlags)
    {
        var tokens = new List<string>();
        if (IsResolved && Location.Source == "arg")
        {
            tokens.Add(DataDirectoryArgument);
            tokens.Add($"\"{Location.Root.Replace("\"", "\\\"")}\"");
        }

        foreach (var flag in extraFlags)
        {
            if (!string.IsNullOrWhiteSpace(flag))
                tokens.Add(flag);
        }

        return string.Join(' ', tokens);
    }

    private static AppDataLocation Create(string root, string source, Func<string, bool> canWrite)
    {
        if (!canWrite(root))
        {
            throw new AppDataLocatorException(
                $"The data folder is not writable:{Environment.NewLine}{root}{Environment.NewLine}{Environment.NewLine}" +
                "Move Nagi to a writable folder, or start it with --datadir pointing at a writable path.");
        }

        return new AppDataLocation(root, source);
    }

    private static string ResolvePath(string baseDirectory, string path)
    {
        var combined = Path.IsPathRooted(path) ? path : Path.Combine(baseDirectory, path);
        return Path.GetFullPath(combined);
    }

    private static bool TryReadPortableConfig(string baseDirectory, out string dataDirectory)
    {
        dataDirectory = string.Empty;
        var configPath = Path.Combine(baseDirectory, PortableConfigFileName);
        if (!File.Exists(configPath)) return false;

        try
        {
            using var stream = File.OpenRead(configPath);
            using var document = JsonDocument.Parse(stream);
            string? value = null;
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (property.Name.Equals("DataDirectory", StringComparison.OrdinalIgnoreCase) ||
                    property.Name.Equals("datadir", StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value.GetString();
                    break;
                }
            }

            if (string.IsNullOrWhiteSpace(value)) return false;

            dataDirectory = value;
            return true;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            throw new AppDataLocatorException($"Failed to read {PortableConfigFileName}: {ex.Message}", ex);
        }
    }

    public static bool TryEnsureWritable(string directory)
    {
        try
        {
            Directory.CreateDirectory(directory);
            var probe = Path.Combine(directory, $".nagi-write-{Guid.NewGuid():N}");
            File.WriteAllText(probe, "ok");
            File.Delete(probe);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return false;
        }
    }
}

/// <summary>
///     Result of resolving the portable data directory.
/// </summary>
public sealed record AppDataLocation(string Root, string Source);

/// <summary>
///     Thrown when the data directory cannot be determined or is not writable.
/// </summary>
public sealed class AppDataLocatorException : Exception
{
    public AppDataLocatorException(string message) : base(message)
    {
    }

    public AppDataLocatorException(string message, Exception inner) : base(message, inner)
    {
    }
}
