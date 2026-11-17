// Version: 0.3.22-preview. Generated, updated and covered: 2026-11-17 (Asia/Shanghai).
// Sources: RAW | user request for visible version and release checks. LOCAL-WORK | Codex Desktop / Lumi Companion / Codex.
using System.Net;
using System.Net.Http;
using LumiCompanion.Infrastructure;

namespace LumiCompanion.Diagnostics;

internal static class GitHubUpdateSelfTest
{
    internal static void Run()
    {
        const string releases = """
            [
              {"tag_name":"v0.3.9", "draft":false},
              {"tag_name":"v0.3.10-beta.9", "prerelease":true},
              {"tag_name":"v0.3.10-beta.10", "prerelease":true, "html_url":"https://example.invalid"},
              {"tag_name":"v9.0.0", "draft":true},
              {"tag_name":"development"}
            ]
            """;
        GitHubUpdateStatus beta = CheckRelease("0.3.10-beta.9", releases);
        Check(beta is { UpdateAvailable: true, LatestVersion: "0.3.10-beta.10", Error: null } &&
              beta.ReleasePage?.AbsoluteUri == "https://github.com/AsaitsukiLunar/lumi-companion-public/releases/tag/v0.3.10-beta.10",
            "recognizes public prereleases by numeric sequence, ignores drafts, and uses the official release page");
        Check(CheckRelease("0.3.9", releases).UpdateAvailable,
            "a newer minor iteration is compared numerically");
        Check(!CheckRelease("0.3.10-preview", releases).UpdateAvailable &&
              !CheckRelease("0.3.20-preview", releases).UpdateAvailable,
            "local previews do not report same-core beta or older public packages as updates");
        Check(CheckRelease("0.3.10-preview", "[{\"tag_name\":\"v0.3.10\"}]").UpdateAvailable,
            "a final release can supersede a local preview of the same version");
        Check(CheckRelease("0.3.10-preview", "[]", HttpStatusCode.TooManyRequests) is { Error: not null, UpdateAvailable: false } &&
              CheckRelease("0.3.10-preview", "broken response") is { Error: not null, UpdateAvailable: false },
            "rate limits and invalid JSON show an unavailable check without an update result");
    }

    private static GitHubUpdateStatus CheckRelease(string version, string body, HttpStatusCode status = HttpStatusCode.OK)
    {
        using var client = new HttpClient(new ReleaseFixture(body, status));
        return new GitHubUpdateService(client).CheckAsync(version, CancellationToken.None).GetAwaiter().GetResult();
    }

    private static void Check(bool condition, string description)
    {
        if (!condition) throw new InvalidOperationException(description);
    }

    private sealed class ReleaseFixture(string body, HttpStatusCode status) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Check(request.Method == HttpMethod.Get && request.RequestUri?.Host == "api.github.com" &&
                  request.RequestUri.AbsolutePath == "/repos/AsaitsukiLunar/lumi-companion-public/releases" &&
                  request.Headers.Authorization is null,
                "update checks use the official public release list");
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });
        }
    }
}
