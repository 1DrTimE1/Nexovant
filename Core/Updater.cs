using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;

namespace Nexovant.Core;

public sealed record UpdateInfo(Version Version, string Tag, string SetupUrl, string Sha256, string Notes);

/// <summary>
/// Auto-update through GitHub Releases. Each release must contain NexovantSetup.exe (built by
/// .github/workflows/release.yml). The download is verified against the SHA-256 digest GitHub reports for the file.
/// </summary>
public static class Updater
{
    /// <summary>GitHub repository in the form "owner/name". The repository must be public.</summary>
    public const string Repository = "1drtime1/Nexovant";

    const string SetupAsset = "NexovantSetup.exe";

    // Must be declared before Http, which uses it in its initializer.
    public static Version CurrentVersion { get; } =
        Assembly.GetExecutingAssembly().GetName().Version is { } v ? new Version(v.Major, v.Minor, v.Build) : new Version(1, 0, 0);

    static readonly HttpClient Http = CreateClient();

    static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Nexovant", CurrentVersion.ToString()));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return client;
    }

    /// <summary>Returns the latest release if it is newer than the running version, otherwise null.</summary>
    public static async Task<UpdateInfo?> CheckAsync(CancellationToken ct = default)
    {
        using var response = await Http.GetAsync($"https://api.github.com/repos/{Repository}/releases/latest", ct);
        if (!response.IsSuccessStatusCode) return null; // no release yet, offline, rate limited…

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        var release = json.RootElement;
        var tag = release.GetProperty("tag_name").GetString() ?? "";
        if (!Version.TryParse(tag.TrimStart('v', 'V'), out var latest) || latest <= CurrentVersion)
            return null;

        foreach (var asset in release.GetProperty("assets").EnumerateArray())
        {
            if (asset.GetProperty("name").GetString() != SetupAsset) continue;

            // GitHub computes this itself when the file is uploaded, e.g. "sha256:4d7e…".
            var digest = asset.TryGetProperty("digest", out var d) ? d.GetString() : null;
            var url = asset.GetProperty("browser_download_url").GetString();
            if (url is null || digest is null || !digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
                return null;

            var notes = release.TryGetProperty("body", out var body) ? body.GetString() ?? "" : "";
            return new UpdateInfo(latest, tag, url, digest["sha256:".Length..].ToLowerInvariant(), notes);
        }
        return null;
    }

    /// <summary>True when this copy was installed by NexovantSetup.exe (dev builds are never auto-updated).</summary>
    public static bool CanSelfUpdate => File.Exists(Path.Combine(AppContext.BaseDirectory, "unins000.exe"));

    static readonly string DownloadDir = Path.Combine(Path.GetTempPath(), "Nexovant-update");

    /// <summary>
    /// Downloads and verifies the new setup, then starts it silently. The setup closes this app, installs
    /// the new version over it and starts it again. The caller should exit right after this returns.
    /// </summary>
    public static async Task InstallAsync(UpdateInfo update, IProgress<double>? progress, CancellationToken ct = default)
    {
        Directory.CreateDirectory(DownloadDir);
        var setup = Path.Combine(DownloadDir, $"NexovantSetup-{update.Version}.exe");

        using (var response = await Http.GetAsync(update.SetupUrl, HttpCompletionOption.ResponseHeadersRead, ct))
        {
            response.EnsureSuccessStatusCode();
            var total = response.Content.Headers.ContentLength ?? 0;
            await using var source = await response.Content.ReadAsStreamAsync(ct);
            await using var target = File.Create(setup);
            var buffer = new byte[81920];
            long read = 0;
            int n;
            while ((n = await source.ReadAsync(buffer, ct)) > 0)
            {
                await target.WriteAsync(buffer.AsMemory(0, n), ct);
                read += n;
                if (total > 0) progress?.Report((double)read / total);
            }
        }

        string actual;
        await using (var file = File.OpenRead(setup))
            actual = Convert.ToHexStringLower(await SHA256.HashDataAsync(file, ct));
        if (actual != update.Sha256)
        {
            File.Delete(setup);
            throw new InvalidDataException("Den nedladdade filen var skadad (kontrollsumman stämmer inte). Uppdateringen avbröts.");
        }

        Process.Start(new ProcessStartInfo(setup, "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS") { UseShellExecute = true });
    }

    /// <summary>Removes setup files left over from earlier updates.</summary>
    public static void CleanupDownloads()
    {
        try
        {
            if (Directory.Exists(DownloadDir)) Directory.Delete(DownloadDir, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // still in use by a running setup, try again next start
        }
    }
}
