using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;

namespace Nagi.Core.Helpers;

/// <summary>
///     Thread-safe JSON key/value store with debounce and atomic writes.
/// </summary>
public sealed class JsonSettingsStore : IDisposable
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true
    };

    private readonly TimeSpan _debounce;
    private readonly string _filePath;
    private readonly object _gate = new();
    private readonly ILogger? _logger;
    private readonly Dictionary<string, JsonNode?> _values = new(StringComparer.Ordinal);
    private Timer? _timer;
    private bool _dirty;
    private bool _disposed;
    private bool _frozen;

    public JsonSettingsStore(string filePath, ILogger? logger = null, TimeSpan? debounce = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        _filePath = filePath;
        _logger = logger;
        _debounce = debounce ?? TimeSpan.FromMilliseconds(400);
        Load();
    }

    public bool IsFrozen
    {
        get
        {
            lock (_gate) return _frozen;
        }
    }

    public T GetValue<T>(string key, T defaultValue)
    {
        lock (_gate)
        {
            if (!_values.TryGetValue(key, out var node) || node is null)
                return defaultValue;

            try
            {
                var result = node.Deserialize<T>();
                return result is null && defaultValue is not null ? defaultValue : result!;
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to read setting '{Key}' as {Type}.", key, typeof(T).Name);
                return defaultValue;
            }
        }
    }

    public bool TryGetRaw(string key, out JsonNode? node)
    {
        lock (_gate)
        {
            return _values.TryGetValue(key, out node);
        }
    }

    public void SetValue<T>(string key, T? value)
    {
        lock (_gate)
        {
            if (_frozen || _disposed) return;

            if (value is null)
            {
                if (!_values.Remove(key)) return;
            }
            else
            {
                _values[key] = JsonSerializer.SerializeToNode(value);
            }

            _dirty = true;
            ScheduleWriteUnlocked();
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            if (_frozen || _disposed) return;
            if (_values.Count == 0) return;
            _values.Clear();
            _dirty = true;
            ScheduleWriteUnlocked();
        }
    }

    /// <summary>
    ///     Stops further writes. Use after restoring files from a backup so the in-memory
    ///     dictionary cannot overwrite restored settings.json before restart.
    /// </summary>
    public void FreezeWrites()
    {
        lock (_gate)
        {
            _frozen = true;
            _dirty = false;
            _timer?.Change(Timeout.Infinite, Timeout.Infinite);
        }
    }

    public void Flush()
    {
        lock (_gate)
        {
            WriteUnlocked();
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            if (!_frozen) WriteUnlocked();
            _timer?.Dispose();
            _timer = null;
        }
    }

    private void Load()
    {
        if (!File.Exists(_filePath)) return;

        try
        {
            var json = File.ReadAllText(_filePath);
            if (string.IsNullOrWhiteSpace(json)) return;

            var root = JsonNode.Parse(json) as JsonObject;
            if (root is null) return;

            foreach (var pair in root)
            {
                _values[pair.Key] = pair.Value?.DeepClone();
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to load settings from {Path}. Using defaults.", _filePath);
            _values.Clear();
        }
    }

    private void ScheduleWriteUnlocked()
    {
        _timer ??= new Timer(OnDebounceElapsed, null, Timeout.Infinite, Timeout.Infinite);
        _timer.Change(_debounce, Timeout.InfiniteTimeSpan);
    }

    private void OnDebounceElapsed(object? state)
    {
        try
        {
            Flush();
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to persist settings to {Path}.", _filePath);
        }
    }

    private void WriteUnlocked()
    {
        if (!_dirty || _frozen) return;

        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var obj = new JsonObject();
        foreach (var pair in _values)
        {
            obj[pair.Key] = pair.Value?.DeepClone();
        }

        var json = obj.ToJsonString(SerializerOptions);
        var tempPath = _filePath + ".tmp";
        File.WriteAllText(tempPath, json);
        File.Move(tempPath, _filePath, overwrite: true);
        _dirty = false;
    }
}
