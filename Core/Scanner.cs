using System.Security;

namespace Nexovant.Core;

public static class Scanner
{
    static readonly EnumerationOptions ListAll = new()
    {
        IgnoreInaccessible = true,
        AttributesToSkip = 0,
        RecurseSubdirectories = false,
    };

    public static CategoryScanResult Scan(CleanupCategory category, CancellationToken ct)
    {
        var result = new CategoryScanResult(category);

        if (category.Kind == CategoryKind.RecycleBin)
        {
            var (size, count) = RecycleBinApi.Query();
            result.TotalBytes = size;
            result.ItemCount = count;
            return result;
        }

        var cutoff = DateTime.UtcNow - category.MinAge;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var root in category.Roots())
        {
            ct.ThrowIfCancellationRequested();
            var path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root.Path));
            if (!seen.Add(path) || !Directory.Exists(path) || !SafetyGuard.IsSafeRoot(path))
                continue;

            if (root.AppProcess is not null && SafetyGuard.IsAppRunning(root.AppProcess))
            {
                result.SkippedApps.Add(root.AppName ?? root.AppProcess);
                continue;
            }

            int rootIndex = result.Roots.Count;
            result.Roots.Add(root with { Path = path });

            foreach (var file in EnumerateFiles(path, ct))
            {
                if (category.MinAge > TimeSpan.Zero && NewestTimestamp(file) > cutoff)
                {
                    result.SkippedRecent++;
                    continue;
                }
                result.Files.Add(new FoundFile(file.FullName, file.Length, rootIndex, file.LastWriteTime));
                result.TotalBytes += file.Length;
            }
        }

        result.ItemCount = result.Files.Count;
        return result;
    }

    internal static DateTime NewestTimestamp(FileSystemInfo info) =>
        info.LastWriteTimeUtc > info.CreationTimeUtc ? info.LastWriteTimeUtc : info.CreationTimeUtc;

    /// <summary>Walks a folder tree without ever following junctions or symbolic links.</summary>
    static IEnumerable<FileInfo> EnumerateFiles(string root, CancellationToken ct)
    {
        var pending = new Stack<DirectoryInfo>();
        pending.Push(new DirectoryInfo(root));

        while (pending.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            var dir = pending.Pop();

            List<FileSystemInfo> entries;
            try
            {
                entries = dir.EnumerateFileSystemInfos("*", ListAll).ToList();
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or SecurityException)
            {
                continue;
            }

            foreach (var entry in entries)
            {
                if ((entry.Attributes & FileAttributes.ReparsePoint) != 0)
                    continue;
                if (entry is DirectoryInfo sub)
                    pending.Push(sub);
                else if (entry is FileInfo file)
                    yield return file;
            }
        }
    }
}
