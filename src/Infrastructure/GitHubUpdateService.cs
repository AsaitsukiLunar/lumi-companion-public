/*
Traceability metadata
Version: 0.3.22-preview
Generated / Last updated / Content coverage through: 2026-11-17 (Asia/Shanghai)
Source type: RAW (user request for GitHub update checks); LOCAL-WORK (Lumi implementation)
Source: Codex Desktop / Lumi Companion / current Lumi conversation (title pending) / Codex
Original sources: user request and the Lumi public release repository
*/

using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace LumiCompanion.Infrastructure;

internal sealed record GitHubUpdateStatus(string? LatestVersion, Uri? ReleasePage, bool UpdateAvailable, string? Error);

internal sealed class GitHubUpdateService
{
    private const string Repository = "AsaitsukiLunar/lumi-companion-public";
    private const int MaxResponseBytes = 4 * 1024 * 1024;
    private static readonly Uri ReleasesEndpoint = new($"https://api.github.com/repos/{Repository}/releases?per_page=30");
    private static readonly HttpClient SharedClient = new(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false })
    {
        MaxResponseContentBufferSize = MaxResponseBytes
    };
    private static readonly Regex VersionPattern = new(
        @"\Av?(?<core>(?:0|[1-9][0-9]*)\.(?:0|[1-9][0-9]*)\.(?:0|[1-9][0-9]*))(?:-(?<stage>alpha|beta|rc|preview)(?:\.(?<number>0|[1-9][0-9]*))?)?(?:\+[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?\z",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase | RegexOptions.NonBacktracking);
    private readonly HttpClient _client;

    internal GitHubUpdateService(HttpClient? client = null) => _client = client ?? SharedClient;

    internal async Task<GitHubUpdateStatus> CheckAsync(string installedVersion, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!ReleaseVersion.TryParse(installedVersion, out ReleaseVersion installed))
            return Failed("无法识别当前版本号。");

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(8));
        using var request = new HttpRequestMessage(HttpMethod.Get, ReleasesEndpoint);
        request.Headers.UserAgent.ParseAdd("LumiCompanion");
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        try
        {
            using HttpResponseMessage response = await _client.SendAsync(request, deadline.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return Failed(response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests
                    ? "GitHub 暂时限制了更新检查，请稍后再试。"
                    : "GitHub 暂时无法返回版本信息。");

            await using Stream body = await response.Content.ReadAsStreamAsync(deadline.Token).ConfigureAwait(false);
            using JsonDocument data = await JsonDocument.ParseAsync(body, cancellationToken: deadline.Token).ConfigureAwait(false);
            if (data.RootElement.ValueKind != JsonValueKind.Array)
                return Failed("GitHub 返回的版本信息格式不正确。");

            ReleaseVersion? newest = null;
            string? newestTag = null;
            foreach (JsonElement release in data.RootElement.EnumerateArray())
            {
                if (release.ValueKind != JsonValueKind.Object ||
                    (release.TryGetProperty("draft", out JsonElement draft) && draft.ValueKind == JsonValueKind.True) ||
                    !release.TryGetProperty("tag_name", out JsonElement tagValue) || tagValue.ValueKind != JsonValueKind.String)
                    continue;

                string tag = tagValue.GetString()!;
                if (!ReleaseVersion.TryParse(tag, out ReleaseVersion version) ||
                    (newest is { } previous && version.CompareTo(previous) <= 0)) continue;
                newest = version;
                newestTag = tag;
            }

            if (newest is not { } latest || newestTag is null)
                return Failed("GitHub 发行页暂时没有可识别的版本。");

            // Construct the browser link from the known repository and verified tag.
            var page = new Uri($"https://github.com/{Repository}/releases/tag/{Uri.EscapeDataString(newestTag)}");
            bool sameTrainPreview = installed.Stage == ReleaseStage.Preview && latest.Core == installed.Core;
            bool updateAvailable = latest.CompareTo(installed) > 0 && (!sameTrainPreview || latest.Stage == ReleaseStage.Stable);
            return new(newestTag.TrimStart('v', 'V'), page, updateAvailable, null);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Failed("检查 GitHub 更新超时，请稍后再试。");
        }
        catch (Exception error) when (error is HttpRequestException or IOException or JsonException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Failed("暂时无法读取 GitHub 版本信息，请稍后再试。");
        }
    }

    private static GitHubUpdateStatus Failed(string error) => new(null, null, false, error);

    private enum ReleaseStage { Alpha, Beta, Preview, ReleaseCandidate, Stable }

    private readonly record struct ReleaseVersion(Version Core, ReleaseStage Stage, int Sequence)
    {
        internal static bool TryParse(string value, out ReleaseVersion release)
        {
            release = default;
            if (string.IsNullOrEmpty(value) || value.Length > 96) return false;
            Match match = VersionPattern.Match(value);
            if (!match.Success || !Version.TryParse(match.Groups["core"].Value, out Version? core)) return false;
            ReleaseStage stage = match.Groups["stage"].Value.ToLowerInvariant() switch
            {
                "alpha" => ReleaseStage.Alpha,
                "beta" => ReleaseStage.Beta,
                "preview" => ReleaseStage.Preview,
                "rc" => ReleaseStage.ReleaseCandidate,
                _ => ReleaseStage.Stable
            };
            Group number = match.Groups["number"];
            int sequence = 0;
            if (number.Success && !int.TryParse(number.Value, out sequence)) return false;
            release = new(core, stage, sequence);
            return true;
        }

        internal int CompareTo(ReleaseVersion other)
        {
            int comparison = Core.CompareTo(other.Core);
            if (comparison != 0) return comparison;
            comparison = Stage.CompareTo(other.Stage);
            return comparison != 0 ? comparison : Sequence.CompareTo(other.Sequence);
        }
    }
}
