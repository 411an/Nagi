using FluentAssertions;
using Nagi.Core.Helpers;
using Xunit;

namespace Nagi.Core.Tests;

public sealed class AppDataLocatorTests : IDisposable
{
    private readonly string _tempRoot;

    public AppDataLocatorTests()
    {
        AppDataLocator.Reset();
        _tempRoot = Path.Combine(Path.GetTempPath(), "nagi-locator-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
    }

    public void Dispose()
    {
        AppDataLocator.Reset();
        try
        {
            if (Directory.Exists(_tempRoot))
                Directory.Delete(_tempRoot, recursive: true);
        }
        catch (IOException)
        {
            // Best-effort cleanup on Windows file locks.
        }
    }

    [Fact]
    public void Resolve_Default_UsesDatadirBesideBase()
    {
        var location = AppDataLocator.Resolve(_tempRoot, Array.Empty<string>());

        location.Source.Should().Be("default");
        location.Root.Should().Be(Path.GetFullPath(Path.Combine(_tempRoot, "datadir")));
        Directory.Exists(location.Root).Should().BeTrue();
        AppDataLocator.Root.Should().Be(location.Root);
    }

    [Fact]
    public void Resolve_DatadirArgument_WinsOverDefault()
    {
        var custom = Path.Combine(_tempRoot, "custom-data");
        var location = AppDataLocator.Resolve(_tempRoot, ["--datadir", custom]);

        location.Source.Should().Be("arg");
        location.Root.Should().Be(Path.GetFullPath(custom));
    }

    [Fact]
    public void Resolve_PortableJson_IsUsedWhenNoArgument()
    {
        var custom = Path.Combine(_tempRoot, "from-json");
        File.WriteAllText(
            Path.Combine(_tempRoot, "portable.json"),
            $$"""{"DataDirectory":"{{custom.Replace("\\", "\\\\")}}"}""");

        var location = AppDataLocator.Resolve(_tempRoot, Array.Empty<string>());

        location.Source.Should().Be("portable.json");
        location.Root.Should().Be(Path.GetFullPath(custom));
    }

    [Fact]
    public void Resolve_ArgumentBeatsPortableJson()
    {
        var fromJson = Path.Combine(_tempRoot, "from-json");
        var fromArg = Path.Combine(_tempRoot, "from-arg");
        File.WriteAllText(
            Path.Combine(_tempRoot, "portable.json"),
            $$"""{"DataDirectory":"{{fromJson.Replace("\\", "\\\\")}}"}""");

        var location = AppDataLocator.Resolve(_tempRoot, ["--datadir", fromArg]);

        location.Source.Should().Be("arg");
        location.Root.Should().Be(Path.GetFullPath(fromArg));
    }

    [Fact]
    public void Resolve_Unwritable_Throws()
    {
        var act = () => AppDataLocator.Resolve(_tempRoot, Array.Empty<string>(), _ => false);

        act.Should().Throw<AppDataLocatorException>()
            .WithMessage("*not writable*");
    }

    [Fact]
    public void Resolve_PortableJson_AcceptsDatadirKey()
    {
        var custom = Path.Combine(_tempRoot, "from-datadir-key");
        File.WriteAllText(
            Path.Combine(_tempRoot, "portable.json"),
            $$"""{"datadir":"{{custom.Replace("\\", "\\\\")}}"}""");

        var location = AppDataLocator.Resolve(_tempRoot, Array.Empty<string>());

        location.Source.Should().Be("portable.json");
        location.Root.Should().Be(Path.GetFullPath(custom));
    }

    [Fact]
    public void BuildRestartArguments_IncludesDatadirWhenSourceIsArg()
    {
        var custom = Path.Combine(_tempRoot, "restart-data");
        AppDataLocator.Resolve(_tempRoot, ["--datadir", custom]);

        var args = AppDataLocator.BuildRestartArguments("--startup");

        args.Should().Contain("--datadir");
        args.Should().Contain(Path.GetFullPath(custom));
        args.Should().Contain("--startup");
    }

    [Fact]
    public void Resolve_SecondCall_ReturnsCachedRoot()
    {
        var first = AppDataLocator.Resolve(_tempRoot, Array.Empty<string>());
        var second = AppDataLocator.Resolve(_tempRoot, ["--datadir", Path.Combine(_tempRoot, "ignored")]);

        second.Root.Should().Be(first.Root);
        second.Source.Should().Be(first.Source);
    }
}
