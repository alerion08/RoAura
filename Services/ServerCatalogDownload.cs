using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using RoAura.Models;

namespace RoAura.Services;

internal static class ServerCatalogDownload
{
    private const string ManifestUrl = "https://raw.githubusercontent.com/alerion08/RoAura/catalogs/manifest.json";
    private const long MaximumArchiveSize = 512L * 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };

    internal static string? ServerKey(string version) =>
        version == GameServerProfile.RagnarokPlus ? "ragnarokplus" :
        version == GameDatabaseVersion.SpringRo ? "springro" : null;

    internal static string InstalledPath(string key) =>
        Path.Combine(AppEnvironment.LocalDirectory, "Catalogs", key + (key == "springro" ? ".json" : ".zip"));

    internal static bool IsInstalled(string version) =>
        ServerKey(version) is { } key && File.Exists(InstalledPath(key));

    internal static void Remove(string key)
    {
        var path = InstalledPath(key);
        if (!File.Exists(path)) return;
        if (key == "ragnarokplus") RagnarokPlusData.RemoveInstalledCatalog(path);
        else File.Delete(path);
    }

    internal static async Task<bool> InstallAsync(string key, IProgress<(int Percent, long SizeBytes)> progress, CancellationToken cancellation)
    {
        var destination = InstalledPath(key);
        if (key == "springro")
        {
            var source = Path.Combine(AppContext.BaseDirectory, "Data", "database.springro.json");
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.Copy(source, temporary);
                cancellation.ThrowIfCancellationRequested();
                File.Move(temporary, destination, overwrite: true);
                return true;
            }
            finally { File.Delete(temporary); }
        }
        var bundled = Path.Combine(AppContext.BaseDirectory, "ragnarokplus-offline.zip");
        if (key == "ragnarokplus" && File.Exists(bundled) &&
            (!File.Exists(destination) || new FileInfo(destination).Length == 210539656))
        {
            using var input = OpenArchive(bundled);
            return await InstallArchiveAsync(key, destination, input, 105718697,
                "f07599ea1a7fd8d66edd899e881efe45eea2a938f5f7c6eae9075793b2b7ec99",
                progress, cancellation);
        }

        using var manifestResponse = await Http.GetAsync(ManifestUrl, cancellation);
        if (manifestResponse.StatusCode == HttpStatusCode.NotFound)
            throw new CatalogUnavailableException();
        manifestResponse.EnsureSuccessStatusCode();
        using var manifest = await manifestResponse.Content.ReadAsStreamAsync(cancellation);
        var index = await JsonSerializer.DeserializeAsync<CatalogIndex>(manifest, JsonOptions, cancellation);
        if (index?.SchemaVersion != 1)
            throw new InvalidDataException("Unsupported catalog manifest.");
        if (index.Servers is null || !index.Servers.TryGetValue(key, out var release))
            throw new CatalogUnavailableException();
        var prefix = "https://github.com/alerion08/RoAura/releases/download/";
        if (!Uri.TryCreate(release.Url, UriKind.Absolute, out var uri) ||
            !uri.AbsoluteUri.StartsWith(prefix, StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(release.Version) || release.SizeBytes <= 0 || release.SizeBytes > MaximumArchiveSize ||
            release.Sha256 is not { Length: 64 } || !release.Sha256.All(Uri.IsHexDigit))
            throw new InvalidDataException("Invalid catalog manifest entry.");

        if (File.Exists(destination))
        {
            using var current = OpenArchive(destination);
            if (current.Length == release.SizeBytes &&
                Convert.ToHexString(await SHA256.HashDataAsync(current, cancellation))
                    .Equals(release.Sha256, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        using var response = await Http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellation);
        response.EnsureSuccessStatusCode();
        using var download = await response.Content.ReadAsStreamAsync(cancellation);
        return await InstallArchiveAsync(key, destination, download, release.SizeBytes, release.Sha256,
            progress, cancellation);
    }

    private static async Task<bool> InstallArchiveAsync(string key, string destination, Stream input,
        long sizeBytes, string sha256, IProgress<(int Percent, long SizeBytes)> progress, CancellationToken cancellation)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var staging = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        deadline.CancelAfter(TimeSpan.FromMinutes(20));
        var token = deadline.Token;
        try
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            using (var output = File.Create(staging))
            {
                var buffer = new byte[128 * 1024];
                long received = 0;
                var lastPercent = -1;
                int count;
                while ((count = await input.ReadAsync(buffer.AsMemory(), token)) > 0)
                {
                    received += count;
                    if (received > sizeBytes)
                        throw new InvalidDataException("Catalog exceeds the published size.");
                    hash.AppendData(buffer, 0, count);
                    await output.WriteAsync(buffer.AsMemory(0, count), token);
                    var percent = (int)(received * 100 / sizeBytes);
                    if (percent != lastPercent)
                    {
                        progress.Report((percent, sizeBytes));
                        lastPercent = percent;
                    }
                }

                if (received != sizeBytes ||
                    !Convert.ToHexString(hash.GetHashAndReset()).Equals(sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Catalog checksum or size mismatch.");
            }

            var catalog = ReadArchive(staging, key);
            if (catalog.Items is not { Count: > 0 } || catalog.Monsters is not { Count: > 0 } ||
                catalog.Maps is not { Count: > 0 } ||
                catalog.Items.Any(item => item is null) || catalog.Monsters.Any(monster => monster is null) ||
                catalog.Maps.Any(map => map is null))
                throw new InvalidDataException("Incomplete catalog.");
            token.ThrowIfCancellationRequested();
            RagnarokPlusData.InstallCatalog(catalog, staging, destination, token);
            return true;
        }
        finally
        {
            try { File.Delete(staging); }
            catch (IOException ex) { DebugLog.Write($"CATALOG DOWNLOAD CLEANUP: {ex.Message}"); }
            catch (UnauthorizedAccessException ex) { DebugLog.Write($"CATALOG DOWNLOAD CLEANUP: {ex.Message}"); }
        }
    }

    internal static GameDatabase ReadArchive(string path, string key)
    {
        using var file = OpenArchive(path);
        using var archive = new ZipArchive(file, ZipArchiveMode.Read);
        var entry = archive.GetEntry($"Data/database.{key}.json");
        if (entry is null || entry.Length <= 0 || entry.Length > MaximumArchiveSize)
            throw new InvalidDataException("Catalog entry missing or too large.");
        using var stream = entry.Open();
        var catalog = JsonSerializer.Deserialize<GameDatabase>(stream, JsonOptions);
        if (catalog is null || !string.Equals(catalog.Source, GameServerProfile.RagnarokPlus, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Catalog belongs to a different server.");
        return catalog;
    }

    private static FileStream OpenArchive(string path) =>
        new(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);

    private sealed record CatalogIndex(int SchemaVersion, Dictionary<string, CatalogRelease> Servers);
    private sealed record CatalogRelease(string Version, string Url, long SizeBytes, string Sha256);
}

internal sealed class CatalogUnavailableException : Exception { }
