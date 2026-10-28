using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RoAura.Services;

public sealed class GitHubUpdateService
{
    private const string LatestReleaseUrl = "https://api.github.com/repos/alerion08/RoAura/releases/latest";
    private static readonly TimeSpan CheckTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan ReadIdleTimeout = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan StaleDownloadAge = TimeSpan.FromDays(7);
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private readonly HttpClient _client;

    public GitHubUpdateService()
    {
        var handler = new SocketsHttpHandler
        {
            ConnectTimeout = TimeSpan.FromSeconds(10),
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            AutomaticDecompression = DecompressionMethods.All
        };
        // HttpClient.Timeout also bounds reading a streamed body, which would cut off
        // a slow installer download. Each call applies its own timeout instead.
        _client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        _client.DefaultRequestHeaders.UserAgent.ParseAdd($"RoAura-Updater/{CurrentVersion.ToString(3)}");
        _client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
    }

    public Version CurrentVersion =>
        Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);

    public async Task<UpdateInfo?> CheckAsync(CancellationToken cancellationToken = default)
    {
        if (AppEnvironment.IsDevelopment) return null;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(CheckTimeout);
        using var response = await _client.GetAsync(LatestReleaseUrl, timeout.Token);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
        var release = await JsonSerializer.DeserializeAsync<GitHubRelease>(stream, JsonOptions, timeout.Token);
        if (release is null || release.Draft || release.Prerelease) return null;

        var tag = release.TagName.Trim().TrimStart('v', 'V');
        if (!Version.TryParse(tag.Split('-', 2)[0], out var latest) || latest <= CurrentVersion)
            return null;

        var installer = release.Assets.FirstOrDefault(asset =>
            asset.Name.EndsWith("-Setup.exe", StringComparison.OrdinalIgnoreCase));
        if (installer is null) return null;

        return new UpdateInfo(latest, release.Name, release.Body, release.HtmlUrl,
            installer.Name, installer.BrowserDownloadUrl, installer.Digest);
    }

    public async Task<string> DownloadInstallerAsync(UpdateInfo update, IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (AppEnvironment.IsDevelopment) throw new InvalidOperationException("Updates disabled in DEV.");
        var updateDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppEnvironment.DataFolderName, "Updates");
        Directory.CreateDirectory(updateDirectory);
        var finalPath = Path.Combine(updateDirectory, update.InstallerName);
        var temporaryPath = finalPath + ".download";
        CleanUpdateDirectory(updateDirectory, finalPath, temporaryPath);

        // The whole download is bounded per chunk, not by HttpClient.Timeout: a large
        // installer on a slow link may legitimately take minutes, but a stalled stream
        // must not hang the update window forever.
        using var idle = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        try
        {
            idle.CancelAfter(ReadIdleTimeout);
            using var response = await _client.GetAsync(update.InstallerUrl,
                HttpCompletionOption.ResponseHeadersRead, idle.Token);
            response.EnsureSuccessStatusCode();
            var length = response.Content.Headers.ContentLength;
            await using (var input = await response.Content.ReadAsStreamAsync(idle.Token))
            await using (var output = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None,
                             81920, useAsync: true))
            {
                var buffer = new byte[81920];
                long received = 0;
                while (true)
                {
                    idle.CancelAfter(ReadIdleTimeout);
                    var read = await input.ReadAsync(buffer, idle.Token);
                    if (read == 0) break;
                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    received += read;
                    if (length > 0) progress?.Report(received * 100d / length.Value);
                }
                idle.CancelAfter(Timeout.InfiniteTimeSpan);
                await output.FlushAsync(cancellationToken);
            }

            var expected = NormalizeDigest(update.Digest);
            if (string.IsNullOrWhiteSpace(expected))
                throw new InvalidDataException("GitHub did not provide a SHA-256 digest for the installer.");
            string actual;
            await using (var downloaded = new FileStream(temporaryPath, FileMode.Open, FileAccess.Read,
                             FileShare.Read, 81920, useAsync: true))
                actual = Convert.ToHexString(await SHA256.HashDataAsync(downloaded, cancellationToken))
                    .ToLowerInvariant();
            if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Downloaded update failed SHA-256 verification.");

            File.Move(temporaryPath, finalPath, overwrite: true);
            progress?.Report(100);
            return finalPath;
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            TryDelete(temporaryPath);
            throw new TimeoutException(
                $"The update download stalled for more than {ReadIdleTimeout.TotalSeconds:F0} seconds.", ex);
        }
        catch
        {
            TryDelete(temporaryPath);
            throw;
        }
    }

    /// <summary>Removes leftovers of earlier updates so the directory does not grow with every release.</summary>
    private static void CleanUpdateDirectory(string directory, string finalPath, string temporaryPath)
    {
        try
        {
            var cutoff = DateTime.UtcNow - StaleDownloadAge;
            foreach (var file in Directory.EnumerateFiles(directory))
            {
                if (file.Equals(finalPath, StringComparison.OrdinalIgnoreCase) ||
                    file.Equals(temporaryPath, StringComparison.OrdinalIgnoreCase)) continue;
                try
                {
                    if (File.GetLastWriteTimeUtc(file) < cutoff) File.Delete(file);
                }
                catch (Exception ex) { DebugLog.Write($"UPDATE CLEANUP SKIPPED {file}: {ex}"); }
            }
        }
        catch (Exception ex) { DebugLog.Write($"UPDATE CLEANUP ERROR: {ex}"); }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (Exception ex) { DebugLog.Write($"UPDATE TEMP DELETE ERROR {path}: {ex}"); }
    }

    public static void StartInstaller(string path)
    {
        if (AppEnvironment.IsDevelopment) throw new InvalidOperationException("Installers disabled in DEV.");
        Process.Start(new ProcessStartInfo(path)
        {
            UseShellExecute = true,
            Arguments = "/VERYSILENT /SUPPRESSMSGBOXES /CLOSEAPPLICATIONS /RESTARTAPPLICATIONS"
        });
    }

    private static string NormalizeDigest(string? digest)
    {
        if (string.IsNullOrWhiteSpace(digest)) return "";
        var value = digest.Trim();
        return value.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) ? value[7..] : value;
    }

    private sealed class GitHubRelease
    {
        [JsonPropertyName("tag_name")] public string TagName { get; set; } = "";
        public string Name { get; set; } = "";
        public string Body { get; set; } = "";
        [JsonPropertyName("html_url")] public string HtmlUrl { get; set; } = "";
        public bool Draft { get; set; }
        public bool Prerelease { get; set; }
        public List<GitHubAsset> Assets { get; set; } = [];
    }

    private sealed class GitHubAsset
    {
        public string Name { get; set; } = "";
        [JsonPropertyName("browser_download_url")] public string BrowserDownloadUrl { get; set; } = "";
        public string? Digest { get; set; }
    }
}

public sealed record UpdateInfo(Version Version, string Name, string Notes, string ReleaseUrl,
    string InstallerName, string InstallerUrl, string? Digest);
