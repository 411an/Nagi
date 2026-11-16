namespace Nagi.WinUI.Models;

/// <summary>
///     Last selected library tree node, restored into the centre browse list on startup.
/// </summary>
public sealed class ShellBrowseState
{
    public string Mode { get; set; } = "Library";

    public string Kind { get; set; } = "Folder";

    public Guid RootFolderId { get; set; }

    public Guid EntityId { get; set; }

    public string Path { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;
}
