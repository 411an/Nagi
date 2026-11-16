using FluentAssertions;
using Nagi.Core.Helpers;
using Xunit;

namespace Nagi.Core.Tests;

public sealed class CommandLineArgsTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly string _host;
    private readonly string _song;

    public CommandLineArgsTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "nagi-args-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
        _host = Path.Combine(_tempRoot, "Nagi.exe");
        _song = Path.Combine(_tempRoot, "track.mp3");
        File.WriteAllText(_host, "host");
        File.WriteAllText(_song, "audio");
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempRoot))
                Directory.Delete(_tempRoot, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public void EmptyArgs_ReturnsNull()
    {
        CommandLineArgs.TryGetFilePath(Array.Empty<string>(), _host).Should().BeNull();
    }

    [Fact]
    public void HostBinaryOnly_ReturnsNull()
    {
        CommandLineArgs.TryGetFilePath([_host], _host).Should().BeNull();
    }

    [Fact]
    public void StartupFlag_IsIgnored()
    {
        CommandLineArgs.TryGetFilePath([_host, "--startup"], _host).Should().BeNull();
    }

    [Fact]
    public void DatadirValue_IsNotTreatedAsFile()
    {
        var data = Path.Combine(_tempRoot, "custom-data");
        Directory.CreateDirectory(data);
        CommandLineArgs.TryGetFilePath([_host, "--datadir", data], _host).Should().BeNull();
    }

    [Fact]
    public void ExistingFile_IsReturned()
    {
        CommandLineArgs.TryGetFilePath([_host, _song], _host).Should().Be(_song);
    }

    [Fact]
    public void FileAfterStartup_IsReturned()
    {
        CommandLineArgs.TryGetFilePath(["--startup", _song], _host).Should().Be(_song);
    }
}
