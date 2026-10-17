using System.Security;

namespace Nexovant.Core;

public static class Cleaner
{
    static readonly EnumerationOptions ListAll = new() { IgnoreInaccessible = false, AttributesToSkip = 0 };

    public static CleanResult Clean(CategoryScanResult scan, CancellationToken ct)
    {
        if (scan.Category.Kind == CategoryKind.RecycleBin)
        {
            var before = RecycleBinApi.Query();
            RecycleBinApi.Empty();
            var after = RecycleBinApi.Query();
            return new CleanResult(Math.Max(0, before.Size - after.Size), (int)Math.Max(0, before.Count - after.Count), (int)after.Count);
        }

        // Re-check every root right before deleting: an app may have been started since the scan.
        var allowedRoots = scan.Roots
            .Select(r => SafetyGuard.IsSafeRoot(r.Path) && (r.AppProcess is null || !SafetyGuard.IsAppRunning(r.AppProcess)))
            .ToArray();

        var checkAge = scan.Category.MinAge > TimeSpan.Zero;
        var cutoff = DateTime.UtcNow - scan.Category.MinAge;
        var verifiedDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var touchedDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long freed = 0;
        int deleted = 0, failed = 0;

        foreach (var file in scan.Files)
        {
            ct.ThrowIfCancellationRequested();
            if (!file.IsSelected) continue; // the user chose to keep this file

            var root = scan.Roots[file.RootIndex].Path;
            if (!allowedRoots[file.RootIndex] || !SafetyGuard.IsUnder(file.Path, root))
            {
                failed++;
                continue;
            }

            try
            {
                var info = new FileInfo(file.Path);
                if (!info.Exists) continue;
                if ((info.Attributes & FileAttributes.ReparsePoint) != 0 || !IsPlainPath(info.DirectoryName!, root, verifiedDirs))
                {
                    failed++;
                    continue;
                }
                if (checkAge && Scanner.NewestTimestamp(info) > cutoff)
                    continue; // changed since the scan, leave it alone

                var length = info.Length;
                if (info.IsReadOnly) info.IsReadOnly = false;
                info.Delete();
                freed += length;
                deleted++;
                touchedDirs.Add(info.DirectoryName!);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or SecurityException)
            {
                failed++; // usually the file is locked by a running program
            }
        }

        RemoveEmptyDirectories(touchedDirs, scan.Roots.Select(r => r.Path).ToList(), checkAge, cutoff);
        return new CleanResult(freed, deleted, failed);
    }

    /// <summary>True if no folder between the root and dir is a junction or symbolic link.</summary>
    static bool IsPlainPath(string dir, string root, HashSet<string> verified)
    {
        var chain = new List<string>();
        var current = dir;
        while (!string.Equals(Path.TrimEndingDirectorySeparator(current), root, StringComparison.OrdinalIgnoreCase))
        {
            if (verified.Contains(current)) break;
            if (!SafetyGuard.IsUnder(current, root)) return false;
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) return false;
            chain.Add(current);
            current = Path.GetDirectoryName(current);
            if (current is null) return false;
        }
        verified.UnionWith(chain);
        return true;
    }

    /// <summary>Removes folders that became empty because we deleted their files. Never removes a root itself.</summary>
    static void RemoveEmptyDirectories(HashSet<string> touched, List<string> roots, bool checkAge, DateTime cutoff)
    {
        foreach (var start in touched.OrderByDescending(d => d.Length))
        {
            var dir = start;
            while (dir is not null && roots.Any(r => SafetyGuard.IsUnder(dir, r)))
            {
                try
                {
                    var info = new DirectoryInfo(dir);
                    if (!info.Exists || (info.Attributes & FileAttributes.ReparsePoint) != 0) break;
                    if (checkAge && info.CreationTimeUtc > cutoff) break;
                    if (info.EnumerateFileSystemInfos("*", ListAll).Any()) break;
                    info.Delete(recursive: false);
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException or SecurityException)
                {
                    break;
                }
                dir = Path.GetDirectoryName(dir);
            }
        }
    }
}
