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
    private readonly HttpClient _client;

    public GitHubUpdateService()
    {
        var handler = new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli
        };
        _client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
        _client.DefaultRequestHeaders.UserAgent.ParseAdd("RoAura-Updater/2.11.0");
        _client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
    }

    public Version CurrentVersion =>
        Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);

    public async Task<UpdateInfo?> CheckAsync(CancellationToken cancellationToken = default)
    {
        using var response = await _client.GetAsync(LatestReleaseUrl, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var release = await JsonSerializer.DeserializeAsync<GitHubRelease>(stream,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }, cancellationToken);
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
        var updateDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RoAura", "Updates");
        Directory.CreateDirectory(updateDirectory);
        var finalPath = Path.Combine(updateDirectory, update.InstallerName);
        var temporaryPath = finalPath + ".download";

        try
        {
            using var response = await _client.GetAsync(update.InstallerUrl,
                HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
            var length = response.Content.Headers.ContentLength;
            await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var output = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None,
                81920, useAsync: true);
            var buffer = new byte[81920];
            long received = 0;
            while (true)
            {
                var read = await input.ReadAsync(buffer, cancellationToken);
                if (read == 0) break;
                await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                received += read;
                if (length > 0) progress?.Report(received * 100d / length.Value);
            }
            await output.FlushAsync(cancellationToken);

            var expected = NormalizeDigest(update.Digest);
            if (string.IsNullOrWhiteSpace(expected))
                throw new InvalidDataException("GitHub did not provide a SHA-256 digest for the installer.");
            var actual = Convert.ToHexString(await SHA256.HashDataAsync(
                File.OpenRead(temporaryPath), cancellationToken)).ToLowerInvariant();
            if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Downloaded update failed SHA-256 verification.");

            File.Move(temporaryPath, finalPath, overwrite: true);
            progress?.Report(100);
            return finalPath;
        }
        catch
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
            throw;
        }
    }

    public static void StartInstaller(string path)
    {
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
