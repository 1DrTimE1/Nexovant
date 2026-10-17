using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;

namespace Nexovant.Core;

public sealed record UpdateInfo(Version Version, string Tag, string ExeUrl, string Sha256, string Notes);

/// <summary>
/// Auto-update through GitHub Releases. Each release must contain Nexovant.exe (built by
/// .github/workflows/release.yml). The download is verified against the SHA-256 digest GitHub reports for the file.
/// </summary>
public static class Updater
{
    /// <summary>GitHub repository in the form "owner/name". The repository must be public.</summary>
    public const string Repository = "1drtime1/Nexovant";

    const string ExeAsset = "Nexovant.exe";

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
            if (asset.GetProperty("name").GetString() != ExeAsset) continue;

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

    /// <summary>True when running as the published single-file exe (updates are not applied to dev builds).</summary>
    public static bool CanSelfUpdate =>
        Environment.ProcessPath is { } path
        && string.Equals(Path.GetFileName(path), ExeAsset, StringComparison.OrdinalIgnoreCase)
        && !File.Exists(Path.Combine(AppContext.BaseDirectory, "Nexovant.dll"));

    /// <summary>Downloads, verifies and installs the update, then starts the new version. The caller should exit afterwards.</summary>
    public static async Task InstallAsync(UpdateInfo update, IProgress<double>? progress, CancellationToken ct = default)
    {
        var current = Environment.ProcessPath ?? throw new InvalidOperationException("Hittar inte programfilen.");
        var dir = Path.GetDirectoryName(current)!;
        var download = Path.Combine(dir, ExeAsset + ".download");
        var old = current + ".old";

        using (var response = await Http.GetAsync(update.ExeUrl, HttpCompletionOption.ResponseHeadersRead, ct))
        {
            response.EnsureSuccessStatusCode();
            var total = response.Content.Headers.ContentLength ?? 0;
            await using var source = await response.Content.ReadAsStreamAsync(ct);
            await using var target = File.Create(download);
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
        await using (var file = File.OpenRead(download))
            actual = Convert.ToHexStringLower(await SHA256.HashDataAsync(file, ct));
        if (actual != update.Sha256)
        {
            File.Delete(download);
            throw new InvalidDataException("Den nedladdade filen var skadad (kontrollsumman stämmer inte). Uppdateringen avbröts.");
        }

        // A running exe cannot be overwritten on Windows, but it can be renamed.
        if (File.Exists(old)) File.Delete(old);
        File.Move(current, old);
        try
        {
            File.Move(download, current);
        }
        catch
        {
            File.Move(old, current); // roll back
            throw;
        }

        Process.Start(new ProcessStartInfo(current) { UseShellExecute = true });
    }

    /// <summary>Removes the previous version left behind by an update.</summary>
    public static void CleanupOldVersion()
    {
        if (Environment.ProcessPath is not { } current) return;
        foreach (var leftover in new[] { current + ".old", Path.Combine(Path.GetDirectoryName(current)!, ExeAsset + ".download") })
        {
            for (int attempt = 0; attempt < 10 && File.Exists(leftover); attempt++)
            {
                try { File.Delete(leftover); }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Thread.Sleep(300); }
            }
        }
    }
}
