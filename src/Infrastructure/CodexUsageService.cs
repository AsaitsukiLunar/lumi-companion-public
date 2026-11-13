/*
Traceability metadata
Version: 0.3.2-preview
Generated: 2026-10-02 (Asia/Shanghai)
Last updated / Content coverage through: 2026-10-30
Source type: RAW | user repair request; LOCAL-WORK | local implementation and observed schemas
Organizing node: Windows / Codex Desktop / Codex / Lumi usage-link subtask
Original sources: existing Lumi service; current local session metadata and rate-limit events;
official protocol field semantics https://learn.chatgpt.com/docs/app-server (not a guarantee of local file schemas).
*/

using System.Globalization;
using System.Text;
using System.Text.Json;
using LumiCompanion.Integration;
using LumiCompanion.Models;

namespace LumiCompanion.Infrastructure;

public sealed class CodexUsageService
{
    private const int TailBytes = 256 * 1024;
    private static readonly TimeSpan SnapshotLifetime = TimeSpan.FromHours(24);
    public static readonly TimeSpan FreshnessWindow = TimeSpan.FromMinutes(10);
    private readonly string _codexRoot;
    private readonly TimeProvider _timeProvider;

    public CodexUsageService(string? codexRoot = null, TimeProvider? timeProvider = null)
    {
        _codexRoot = codexRoot ?? Environment.GetEnvironmentVariable("CODEX_HOME") ??
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex");
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    // Only observes existing local records. No auth.json, network call or child process is used.
    public Task<CodexUsageSnapshot> ReadAsync(CancellationToken cancellationToken) => Task.Run(() =>
    {
        try { return ReadLocalSnapshot(cancellationToken); }
        catch (OperationCanceledException) { return CodexUsageSnapshot.Unavailable("Codex 快照读取已取消。"); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return CodexUsageSnapshot.Unavailable("本地 Codex 快照暂不可读；稍后会重试。");
        }
    }, CancellationToken.None);

    private CodexUsageSnapshot ReadLocalSnapshot(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        DateTimeOffset now = _timeProvider.GetUtcNow();
        if (LocalCodexAccountScope.Resolve(_codexRoot) is not { } scope)
            return CodexUsageSnapshot.Unavailable("尚未找到桌面 Codex 的账号标识；无法关联额度快照。");

        // Keep raw account/user identifiers out of exported snapshots and UI.
        string scopeKey = scope.ScopeKey;
        CodexUsageSnapshot? latest = null;
        // A resumed chat keeps its original date directory. Select by file modification metadata
        // across session folders, then read at most 32 bounded tails; never parse all chat histories.
        string sessionsRoot = Path.Combine(_codexRoot, "sessions");
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint
        };
        IEnumerable<FileInfo> candidates = Directory.Exists(sessionsRoot)
            ? new DirectoryInfo(sessionsRoot).EnumerateFiles("*.jsonl", options)
            : Enumerable.Empty<FileInfo>();
        foreach (FileInfo file in candidates.OrderByDescending(file => file.LastWriteTimeUtc).Take(32))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                using var stream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete);
                using (var head = new StreamReader(stream, Encoding.UTF8, true, 4096, leaveOpen: true))
                {
                    string? firstLine = head.ReadLine();
                    if (firstLine is null || firstLine.Length > 1024 * 1024) continue;
                    using var metadata = JsonDocument.Parse(firstLine);
                    if (!TryString(metadata.RootElement, "type", out var type) || type != "session_meta" ||
                        !metadata.RootElement.TryGetProperty("payload", out var payload) ||
                        !TryString(payload, "creator_account_id", out var creatorAccount) || creatorAccount != scope.AccountId ||
                        !TryString(payload, "creator_user_id", out var creatorUser) ||
                        scope.AccountUserId != creatorUser + "__" + creatorAccount) continue;
                }

                long offset = Math.Max(0, stream.Length - TailBytes);
                stream.Seek(offset, SeekOrigin.Begin);
                using var reader = new StreamReader(stream, Encoding.UTF8, true, 4096, leaveOpen: true);
                if (offset > 0) reader.ReadLine(); // Skip partial first line, including split UTF-8 characters.
                string? line;
                while ((line = reader.ReadLine()) is not null)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!line.Contains("\"token_count\"", StringComparison.Ordinal) ||
                        !line.Contains("\"rate_limits\"", StringComparison.Ordinal)) continue;
                    try
                    {
                        using var document = JsonDocument.Parse(line);
                        var root = document.RootElement;
                        if (!TryString(root, "type", out var type) || type != "event_msg" ||
                            !TryString(root, "timestamp", out var timestamp) ||
                            !DateTimeOffset.TryParse(timestamp, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var capturedAt) ||
                            capturedAt > now.AddMinutes(1) || now - capturedAt > SnapshotLifetime ||
                            !root.TryGetProperty("payload", out var payload) ||
                            !TryString(payload, "type", out var eventType) || eventType != "token_count" ||
                            !payload.TryGetProperty("rate_limits", out var limits) ||
                            !TryString(limits, "limit_id", out var limitId) || limitId != "codex") continue;

                        CodexUsageSnapshot snapshot = ParseLimits(limits, capturedAt, now) with
                        {
                            SourceLabel = "本地 Codex 限额快照",
                            AccountScopeKey = scopeKey,
                            IsStale = now - capturedAt > FreshnessWindow,
                            StatusMessage = now - capturedAt > FreshnessWindow ? "旧快照；等待 Codex 新的额度观测" : "已关联本地 Codex 快照",
                            TodayStatus = "今日账号 Token 未读取；会话 Token 不代替账号日统计"
                        };
                        if (latest is null || snapshot.CapturedAt > latest.CapturedAt) latest = snapshot;
                    }
                    catch (JsonException) { /* Concurrently appended/malformed lines are not quota values. */ }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                // One inaccessible or half-written session must not hide valid current-account records.
            }
        }

        return latest ?? CodexUsageSnapshot.Unavailable("当前账号暂无可关联的近期额度快照；请在 Codex 中完成一次交互后刷新。") with
        {
            AccountScopeKey = scopeKey,
            SourceLabel = "本地 Codex 限额快照"
        };
    }

    public static CodexUsageSnapshot ParseSnapshot(JsonElement response)
    {
        if (response.ValueKind != JsonValueKind.Object || !response.TryGetProperty("result", out var result) ||
            result.ValueKind != JsonValueKind.Object)
            return CodexUsageSnapshot.Unavailable("Codex 返回的数据不完整。");

        JsonElement limits;
        if (result.TryGetProperty("rateLimitsByLimitId", out var byId) && byId.ValueKind == JsonValueKind.Object)
        {
            // An explicit multi-bucket map must never fall back to a different metered bucket.
            if (!byId.TryGetProperty("codex", out limits))
                return CodexUsageSnapshot.Unavailable("Codex 没有返回 codex 额度窗口。");
        }
        else if (!result.TryGetProperty("rateLimits", out limits))
            return CodexUsageSnapshot.Unavailable("Codex 没有返回可显示的额度窗口。");
        if (TryString(limits, "limitId", out var limitId) && limitId != "codex")
            return CodexUsageSnapshot.Unavailable("返回的额度窗口不属于 Codex。");

        DateTimeOffset now = DateTimeOffset.Now;
        return ParseLimits(limits, now, now);
    }

    private static CodexUsageSnapshot ParseLimits(JsonElement limits, DateTimeOffset capturedAt, DateTimeOffset now)
    {
        var windows = new List<CodexUsageWindow>(2);
        AddWindow(limits, "primary", windows, now);
        AddWindow(limits, "secondary", windows, now);
        var fiveHour = windows.FirstOrDefault(window => window.WindowDurationMinutes == 300);
        var weekly = windows.FirstOrDefault(window => window.WindowDurationMinutes == 10080);
        return new CodexUsageSnapshot(fiveHour, weekly, capturedAt,
            fiveHour is not null || weekly is not null ? "已读取额度窗口" : "额度窗口未知或已过重置时间；等待新快照");
    }

    public static long? ParseTodayTokens(JsonElement response, DateOnly today)
    {
        if (response.ValueKind != JsonValueKind.Object || !response.TryGetProperty("result", out var result) ||
            result.ValueKind != JsonValueKind.Object || !result.TryGetProperty("dailyUsageBuckets", out var buckets) ||
            buckets.ValueKind != JsonValueKind.Array) return null;
        var matching = buckets.EnumerateArray().Where(bucket =>
            TryString(bucket, "startDate", out var date) && date == today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).ToArray();
        return matching.Length == 1 && matching[0].TryGetProperty("tokens", out var tokens) &&
            tokens.ValueKind == JsonValueKind.Number && tokens.TryGetInt64(out long value) && value >= 0 ? value : null;
    }

    private static void AddWindow(JsonElement limits, string propertyName, ICollection<CodexUsageWindow> target, DateTimeOffset now)
    {
        if (limits.ValueKind != JsonValueKind.Object || !limits.TryGetProperty(propertyName, out var window) ||
            window.ValueKind != JsonValueKind.Object ||
            !TryProperty(window, "usedPercent", "used_percent", out var used) || used.ValueKind != JsonValueKind.Number ||
            !used.TryGetDouble(out double percent) || !double.IsFinite(percent) || percent < 0 || percent > 100 ||
            !TryProperty(window, "windowDurationMins", "window_minutes", out var duration) || duration.ValueKind != JsonValueKind.Number ||
            !duration.TryGetInt32(out int minutes) || minutes <= 0) return;

        DateTimeOffset? resetsAt = null;
        if (TryProperty(window, "resetsAt", "resets_at", out var reset) &&
            reset.ValueKind == JsonValueKind.Number && reset.TryGetInt64(out long seconds))
        {
            try { resetsAt = DateTimeOffset.FromUnixTimeSeconds(seconds); }
            catch (ArgumentOutOfRangeException) { }
        }
        if (resetsAt.HasValue && resetsAt <= now) return;
        target.Add(new CodexUsageWindow(minutes, (int)Math.Ceiling(percent), resetsAt));
    }

    private static bool TryProperty(JsonElement element, string camelName, string snakeName, out JsonElement value) =>
        element.TryGetProperty(camelName, out value) || element.TryGetProperty(snakeName, out value);

    private static bool TryString(JsonElement element, string property, out string value)
    {
        value = string.Empty;
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(property, out var field) ||
            field.ValueKind != JsonValueKind.String) return false;
        value = field.GetString() ?? string.Empty;
        return value.Length > 0;
    }
}
