using FluentAssertions;
using Nagi.Core.Helpers;
using Xunit;

namespace Nagi.Core.Tests;

public sealed class JsonSettingsStoreTests : IDisposable
{
    private readonly string _directory;
    private readonly string _filePath;

    public JsonSettingsStoreTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "nagi-settings-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        _filePath = Path.Combine(_directory, "settings.json");
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_directory))
                Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public void GetValue_MissingKey_ReturnsDefault()
    {
        using var store = new JsonSettingsStore(_filePath, debounce: TimeSpan.Zero);
        store.GetValue("missing", 42).Should().Be(42);
    }

    [Fact]
    public void SetAndGet_RoundTripsPrimitives()
    {
        using (var store = new JsonSettingsStore(_filePath, debounce: TimeSpan.FromHours(1)))
        {
            store.SetValue("AppMuteState", true);
            store.SetValue("AppVolume", 0.8);
            store.SetValue("AppTheme", "Dark");
            store.SetValue("SongsPerPage", 50);
            store.Flush();
        }

        using var reloaded = new JsonSettingsStore(_filePath, debounce: TimeSpan.FromHours(1));
        reloaded.GetValue("AppMuteState", false).Should().BeTrue();
        reloaded.GetValue("AppVolume", 0.0).Should().BeApproximately(0.8, 0.0001);
        reloaded.GetValue("AppTheme", "").Should().Be("Dark");
        reloaded.GetValue("SongsPerPage", 0).Should().Be(50);
    }

    [Fact]
    public void ConcurrentSets_FlushOnceWithoutThrowing()
    {
        using var store = new JsonSettingsStore(_filePath, debounce: TimeSpan.FromHours(1));
        Parallel.For(0, 48, i => store.SetValue($"Key{i}", i));
        store.Flush();

        using var reloaded = new JsonSettingsStore(_filePath, debounce: TimeSpan.FromHours(1));
        reloaded.GetValue("Key0", -1).Should().Be(0);
        reloaded.GetValue("Key47", -1).Should().Be(47);
    }

    [Fact]
    public void CorruptFile_DoesNotThrow_UsesDefaults()
    {
        File.WriteAllText(_filePath, "{ not json");
        using var store = new JsonSettingsStore(_filePath, debounce: TimeSpan.FromHours(1));
        store.GetValue("AppTheme", "Light").Should().Be("Light");
    }

    [Fact]
    public void FreezeWrites_BlocksLaterSets()
    {
        using var store = new JsonSettingsStore(_filePath, debounce: TimeSpan.FromHours(1));
        store.SetValue("AppTheme", "Dark");
        store.Flush();
        store.FreezeWrites();
        store.SetValue("AppTheme", "Light");
        store.Flush();

        using var reloaded = new JsonSettingsStore(_filePath, debounce: TimeSpan.FromHours(1));
        reloaded.GetValue("AppTheme", "").Should().Be("Dark");
    }

    [Fact]
    public void Clear_RemovesKeysFromDisk()
    {
        using (var store = new JsonSettingsStore(_filePath, debounce: TimeSpan.FromHours(1)))
        {
            store.SetValue("AppTheme", "Dark");
            store.Flush();
            store.Clear();
            store.Flush();
        }

        using var reloaded = new JsonSettingsStore(_filePath, debounce: TimeSpan.FromHours(1));
        reloaded.GetValue("AppTheme", "fallback").Should().Be("fallback");
    }
}
