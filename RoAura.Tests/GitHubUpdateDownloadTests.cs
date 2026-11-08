using System.Net;
using System.Net.Http;
using RoAura.Services;
using Xunit;

namespace RoAura.Tests;

public sealed class GitHubUpdateDownloadTests
{
    private static readonly UpdateInfo Update = new(new Version(3, 5, 0), "", "", "",
        "RoAura-3.5.0-Setup.exe", "https://github.com/alerion08/RoAura/releases/download/v3.5.0/RoAura-3.5.0-Setup.exe",
        "test-digest") { InstallerAssetId = 723526764 };

    [Theory]
    [InlineData(HttpStatusCode.GatewayTimeout)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public async Task Transient_browser_failure_uses_the_binary_asset_api(HttpStatusCode status)
    {
        using var handler = new DownloadHandler(status);
        using var client = new HttpClient(handler);
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");

        using var response = await GitHubUpdateService.OpenInstallerAsync(client, Update, CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal(Update.InstallerUrl, handler.Requests[0].Url);
        Assert.Equal(("https://api.github.com/repos/alerion08/RoAura/releases/assets/723526764",
            "application/octet-stream"), handler.Requests[1]);
    }

    [Fact]
    public async Task Successful_browser_download_needs_no_fallback()
    {
        using var handler = new DownloadHandler(HttpStatusCode.OK);
        using var client = new HttpClient(handler);
        using var response = await GitHubUpdateService.OpenInstallerAsync(client, Update, CancellationToken.None);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task Permanent_browser_failure_is_returned_without_a_retry()
    {
        using var handler = new DownloadHandler(HttpStatusCode.NotFound);
        using var client = new HttpClient(handler);
        var error = await Assert.ThrowsAsync<HttpRequestException>(() =>
            GitHubUpdateService.OpenInstallerAsync(client, Update, CancellationToken.None));
        Assert.Equal(HttpStatusCode.NotFound, error.StatusCode);
        Assert.Single(handler.Requests);
    }

    private sealed class DownloadHandler(HttpStatusCode primaryStatus) : HttpMessageHandler
    {
        public List<(string Url, string Accept)> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add((request.RequestUri!.AbsoluteUri, request.Headers.Accept.ToString()));
            return Task.FromResult(new HttpResponseMessage(
                Requests.Count == 1 ? primaryStatus : HttpStatusCode.OK));
        }
    }
}
