using System.Diagnostics;
using System.Security.Principal;

namespace Nexovant.Core;

/// <summary>Last line of defence: refuses to clean anything outside known cache/temp folders.</summary>
public static class SafetyGuard
{
    static readonly string[] ProtectedPaths = BuildProtectedPaths();

    static string[] BuildProtectedPaths()
    {
        var list = new List<string>();
        foreach (var drive in DriveInfo.GetDrives())
            list.Add(drive.RootDirectory.FullName);

        Environment.SpecialFolder[] folders =
        [
            Environment.SpecialFolder.Windows, Environment.SpecialFolder.System, Environment.SpecialFolder.SystemX86,
            Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86,
            Environment.SpecialFolder.UserProfile, Environment.SpecialFolder.Desktop, Environment.SpecialFolder.MyDocuments,
            Environment.SpecialFolder.MyPictures, Environment.SpecialFolder.MyMusic, Environment.SpecialFolder.MyVideos,
            Environment.SpecialFolder.ApplicationData, Environment.SpecialFolder.LocalApplicationData,
            Environment.SpecialFolder.CommonApplicationData,
        ];
        foreach (var f in folders)
        {
            var path = Environment.GetFolderPath(f);
            if (!string.IsNullOrEmpty(path)) list.Add(path);
        }
        list.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"));
        if (Environment.GetEnvironmentVariable("OneDrive") is { Length: > 0 } oneDrive) list.Add(oneDrive);

        return list.Select(Normalize).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    static string Normalize(string path) => path.TrimEnd('\\', '/');

    public static bool IsUnder(string path, string root) =>
        path.StartsWith(Normalize(root) + "\\", StringComparison.OrdinalIgnoreCase);

    /// <summary>A root is safe if it is not a protected folder, does not contain one, and is not a link.</summary>
    public static bool IsSafeRoot(string root)
    {
        if (!Path.IsPathFullyQualified(root)) return false;
        var r = Normalize(Path.GetFullPath(root));
        foreach (var p in ProtectedPaths)
        {
            if (string.Equals(r, p, StringComparison.OrdinalIgnoreCase) || IsUnder(p, r))
                return false;
        }
        try
        {
            return (File.GetAttributes(r) & FileAttributes.ReparsePoint) == 0;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public static bool IsAppRunning(string processName)
    {
        var processes = Process.GetProcessesByName(processName);
        foreach (var p in processes) p.Dispose();
        return processes.Length > 0;
    }
}

public static class Elevation
{
    public static bool IsAdmin { get; } =
        new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);
}
