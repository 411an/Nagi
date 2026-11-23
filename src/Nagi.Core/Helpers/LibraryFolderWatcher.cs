using Nagi.Core.Constants;

namespace Nagi.Core.Helpers;

internal sealed class LibraryFolderWatcher : IDisposable
{
    private readonly Action _changed;
    private readonly Action<Exception> _error;
    private FileSystemWatcher? _directoryWatcher;
    private FileSystemWatcher? _fileWatcher;
    private int _disposed;

    public LibraryFolderWatcher(string path, Action changed, Action<Exception> error)
    {
        _changed = changed ?? throw new ArgumentNullException(nameof(changed));
        _error = error ?? throw new ArgumentNullException(nameof(error));

        try
        {
            _directoryWatcher = new FileSystemWatcher(path)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.DirectoryName,
                InternalBufferSize = 64 * 1024
            };
            _directoryWatcher.Created += OnDirectoryChanged;
            _directoryWatcher.Deleted += OnDirectoryChanged;
            _directoryWatcher.Renamed += OnDirectoryRenamed;
            _directoryWatcher.Error += OnError;

            _fileWatcher = new FileSystemWatcher(path)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
                InternalBufferSize = 64 * 1024
            };
            _fileWatcher.Created += OnFileChanged;
            _fileWatcher.Changed += OnFileChanged;
            _fileWatcher.Deleted += OnFileChanged;
            _fileWatcher.Renamed += OnFileRenamed;
            _fileWatcher.Error += OnError;

            _directoryWatcher.EnableRaisingEvents = true;
            _fileWatcher.EnableRaisingEvents = true;
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    internal static bool IsRelevantFile(string path)
    {
        var extension = Path.GetExtension(path);
        return FileExtensions.MusicFileExtensions.Contains(extension) ||
               extension.Equals(".cue", StringComparison.OrdinalIgnoreCase);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

        DisposeWatcher(_directoryWatcher, OnDirectoryChanged, OnDirectoryRenamed);
        DisposeWatcher(_fileWatcher, OnFileChanged, OnFileRenamed);
        _directoryWatcher = null;
        _fileWatcher = null;
    }

    private void OnDirectoryChanged(object sender, FileSystemEventArgs e) => NotifyChanged();

    private void OnDirectoryRenamed(object sender, RenamedEventArgs e) => NotifyChanged();

    private void OnFileChanged(object sender, FileSystemEventArgs e)
    {
        if (IsRelevantFile(e.FullPath)) NotifyChanged();
    }

    private void OnFileRenamed(object sender, RenamedEventArgs e)
    {
        if (IsRelevantFile(e.OldFullPath) || IsRelevantFile(e.FullPath)) NotifyChanged();
    }

    private void NotifyChanged()
    {
        if (Volatile.Read(ref _disposed) == 0) _changed();
    }

    private void OnError(object sender, ErrorEventArgs e)
    {
        if (Volatile.Read(ref _disposed) == 0) _error(e.GetException());
    }

    private void DisposeWatcher(
        FileSystemWatcher? watcher,
        FileSystemEventHandler changedHandler,
        RenamedEventHandler renamedHandler)
    {
        if (watcher is null) return;
        watcher.EnableRaisingEvents = false;
        watcher.Created -= changedHandler;
        watcher.Deleted -= changedHandler;
        watcher.Changed -= changedHandler;
        watcher.Renamed -= renamedHandler;
        watcher.Error -= OnError;
        watcher.Dispose();
    }
}
