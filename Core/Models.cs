using System.ComponentModel;

namespace Nexovant.Core;

public enum CategoryKind { Folders, RecycleBin }

/// <summary>A folder that is allowed to be cleaned. If AppProcess is set, the folder is skipped while that app is running.</summary>
public sealed record CleanupRoot(string Path, string? AppProcess = null, string? AppName = null);

public sealed class CleanupCategory
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }
    public string? Warning { get; init; }
    public CategoryKind Kind { get; init; } = CategoryKind.Folders;
    public Func<IEnumerable<CleanupRoot>> Roots { get; init; } = () => [];
    /// <summary>Files newer than this are never touched (they may still be in use).</summary>
    public TimeSpan MinAge { get; init; } = TimeSpan.Zero;
    public bool RequiresAdmin { get; init; }
    public bool DefaultSelected { get; init; } = true;
}

/// <summary>A file the scan found. The user decides per file (IsSelected) whether it gets removed.</summary>
public sealed class FoundFile(string path, long size, int rootIndex, DateTime lastWrite) : INotifyPropertyChanged
{
    bool isSelected = true;

    public string Path { get; } = path;
    public long Size { get; } = size;
    public int RootIndex { get; } = rootIndex;
    public DateTime LastWrite { get; } = lastWrite;
    public string SizeText => Format.Bytes(Size);
    public string Name => global::System.IO.Path.GetFileName(Path);
    public string Folder => global::System.IO.Path.GetDirectoryName(Path) ?? "";

    public bool IsSelected
    {
        get => isSelected;
        set
        {
            if (isSelected == value) return;
            isSelected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

public sealed class CategoryScanResult(CleanupCategory category)
{
    public CleanupCategory Category { get; } = category;
    public List<CleanupRoot> Roots { get; } = [];
    public List<FoundFile> Files { get; } = [];
    public long TotalBytes { get; set; }
    public long ItemCount { get; set; }
    public int SkippedRecent { get; set; }
    public SortedSet<string> SkippedApps { get; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed record CleanResult(long FreedBytes, int Deleted, int Failed);

public static class Format
{
    public static string Bytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = bytes;
        int i = 0;
        while (value >= 1024 && i < units.Length - 1) { value /= 1024; i++; }
        return i == 0 ? $"{bytes} B" : $"{value:0.0} {units[i]}";
    }
}
