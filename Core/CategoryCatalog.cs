namespace Nexovant.Core;

/// <summary>
/// Every place Nexovant is allowed to clean. Only folders whose content is pure cache or leftovers
/// that Windows or the owning app recreates automatically belong here.
/// </summary>
public static class CategoryCatalog
{
    static readonly string Local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    static readonly string Roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
    static readonly string LocalLow = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "AppData", "LocalLow");
    static readonly string ProgramData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
    static readonly string WinDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);

    // Cache folders inside a Chromium/Electron profile. Cookies, logins, history etc. live elsewhere.
    static readonly string[] ChromiumCacheDirs = ["Cache", "Code Cache", "GPUCache", "DawnCache", "DawnGraphiteCache", "DawnWebGPUCache"];
    static readonly string[] ChromiumSharedCacheDirs = ["ShaderCache", "GrShaderCache", "GraphiteDawnCache"];

    public static IReadOnlyList<CleanupCategory> Create() =>
    [
        new()
        {
            Id = "user-temp",
            Name = "Tillfälliga filer",
            Description = "Temporära filer som program har lämnat kvar i din Temp-mapp. Filer nyare än 24 timmar och filer som används lämnas orörda.",
            Roots = () => [new(Path.GetTempPath())],
            MinAge = TimeSpan.FromHours(24),
        },
        new()
        {
            Id = "windows-temp",
            Name = "Windows tillfälliga filer",
            Description = "Temporära filer i Windows\\Temp. Filer nyare än 24 timmar lämnas orörda.",
            Roots = () => [new(Path.Combine(WinDir, "Temp"))],
            MinAge = TimeSpan.FromHours(24),
            RequiresAdmin = true,
        },
        new()
        {
            Id = "browser-cache",
            Name = "Webbläsarcache",
            Description = "Cachade bilder och skript från Chrome, Edge, Brave, Opera, Vivaldi och Firefox. Inloggningar, lösenord, historik och bokmärken påverkas inte.",
            Warning = "Webbläsare som är öppna hoppas över – stäng dem först för att rensa allt.",
            Roots = BrowserRoots,
        },
        new()
        {
            Id = "app-cache",
            Name = "App-cache",
            Description = "Cache från Discord, Microsoft Teams (klassisk), Visual Studio Code och Slack. Meddelanden och inställningar påverkas inte.",
            Warning = "Appar som är öppna hoppas över.",
            Roots = AppRoots,
        },
        new()
        {
            Id = "shader-cache",
            Name = "Grafik-cache (shaders)",
            Description = "DirectX- och grafikkortscache som byggs upp automatiskt igen. Spel kan starta något långsammare första gången efteråt.",
            Roots = ShaderRoots,
        },
        new()
        {
            Id = "crash-reports",
            Name = "Kraschrapporter och minnesdumpar",
            Description = "Felrapporter från Windows och minnesdumpar från program som har kraschat.",
            Roots = CrashRoots,
        },
        new()
        {
            Id = "windows-update",
            Name = "Windows Update-nedladdningar",
            Description = "Uppdateringsfiler som redan har laddats ner. Windows hämtar dem igen om de behövs. Filer nyare än 10 dagar lämnas orörda.",
            Roots = () => [new(Path.Combine(WinDir, "SoftwareDistribution", "Download"))],
            MinAge = TimeSpan.FromDays(10),
            RequiresAdmin = true,
            DefaultSelected = false,
        },
        new()
        {
            Id = "dev-cache",
            Name = "Utvecklarcache",
            Description = "Nedladdningscache för npm, pip, Yarn och NuGet. Paket laddas ner igen vid behov.",
            Roots = () =>
            [
                new(Path.Combine(Local, "npm-cache")),
                new(Path.Combine(Local, "pip", "cache")),
                new(Path.Combine(Local, "Yarn", "Cache")),
                new(Path.Combine(Local, "NuGet", "v3-cache")),
            ],
            DefaultSelected = false,
        },
        new()
        {
            Id = "recycle-bin",
            Name = "Papperskorgen",
            Kind = CategoryKind.RecycleBin,
            Description = "Filer som du redan har slängt i papperskorgen.",
            Warning = "Filerna tas bort permanent och kan inte återställas.",
            DefaultSelected = false,
        },
    ];

    static IEnumerable<CleanupRoot> BrowserRoots()
    {
        (string UserData, string Process, string Name)[] chromium =
        [
            (Path.Combine(Local, "Google", "Chrome", "User Data"), "chrome", "Chrome"),
            (Path.Combine(Local, "Microsoft", "Edge", "User Data"), "msedge", "Edge"),
            (Path.Combine(Local, "BraveSoftware", "Brave-Browser", "User Data"), "brave", "Brave"),
            (Path.Combine(Local, "Vivaldi", "User Data"), "vivaldi", "Vivaldi"),
        ];
        foreach (var (userData, process, name) in chromium)
        {
            foreach (var dir in ChromiumSharedCacheDirs)
                yield return new(Path.Combine(userData, dir), process, name);
            foreach (var profile in SafeSubdirectories(userData).Where(IsChromiumProfile))
                foreach (var dir in ChromiumCacheDirs)
                    yield return new(Path.Combine(profile, dir), process, name);
        }

        foreach (var operaDir in new[] { Path.Combine(Local, "Opera Software", "Opera Stable"), Path.Combine(Roaming, "Opera Software", "Opera Stable") })
            foreach (var dir in ChromiumCacheDirs.Concat(ChromiumSharedCacheDirs))
                yield return new(Path.Combine(operaDir, dir), "opera", "Opera");

        foreach (var profile in SafeSubdirectories(Path.Combine(Local, "Mozilla", "Firefox", "Profiles")))
        {
            yield return new(Path.Combine(profile, "cache2"), "firefox", "Firefox");
            yield return new(Path.Combine(profile, "startupCache"), "firefox", "Firefox");
        }
    }

    static IEnumerable<CleanupRoot> AppRoots()
    {
        (string Dir, string Process, string Name)[] apps =
        [
            (Path.Combine(Roaming, "discord"), "Discord", "Discord"),
            (Path.Combine(Roaming, "Microsoft", "Teams"), "Teams", "Teams"),
            (Path.Combine(Roaming, "Code"), "Code", "VS Code"),
            (Path.Combine(Roaming, "Slack"), "slack", "Slack"),
        ];
        foreach (var (dir, process, name) in apps)
        {
            foreach (var cache in ChromiumCacheDirs.Concat(ChromiumSharedCacheDirs))
                yield return new(Path.Combine(dir, cache), process, name);
            if (name == "VS Code")
                yield return new(Path.Combine(dir, "CachedData"), process, name);
        }
    }

    static IEnumerable<CleanupRoot> ShaderRoots() =>
    [
        new(Path.Combine(Local, "D3DSCache")),
        new(Path.Combine(Local, "NVIDIA", "DXCache")),
        new(Path.Combine(Local, "NVIDIA", "GLCache")),
        new(Path.Combine(LocalLow, "NVIDIA", "PerDriverVersion", "DXCache")),
        new(Path.Combine(Local, "AMD", "DxCache")),
        new(Path.Combine(Local, "AMD", "DxcCache")),
        new(Path.Combine(Local, "AMD", "VkCache")),
        new(Path.Combine(Local, "Intel", "ShaderCache")),
    ];

    static IEnumerable<CleanupRoot> CrashRoots()
    {
        yield return new(Path.Combine(Local, "CrashDumps"));
        yield return new(Path.Combine(Local, "Microsoft", "Windows", "WER", "ReportArchive"));
        yield return new(Path.Combine(Local, "Microsoft", "Windows", "WER", "ReportQueue"));
        if (!Elevation.IsAdmin) yield break;
        yield return new(Path.Combine(ProgramData, "Microsoft", "Windows", "WER", "ReportArchive"));
        yield return new(Path.Combine(ProgramData, "Microsoft", "Windows", "WER", "ReportQueue"));
        yield return new(Path.Combine(WinDir, "Minidump"));
        yield return new(Path.Combine(WinDir, "LiveKernelReports"));
    }

    static bool IsChromiumProfile(string dir)
    {
        var name = Path.GetFileName(dir);
        return name is "Default" or "Guest Profile" or "System Profile" || name.StartsWith("Profile ", StringComparison.Ordinal);
    }

    static List<string> SafeSubdirectories(string dir)
    {
        try
        {
            return Directory.Exists(dir) ? Directory.EnumerateDirectories(dir).ToList() : [];
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }
}
