// Version: 0.3.2-preview; Generated/Updated/Coverage: 2026-10-30 (Asia/Shanghai)
// Source type: LOCAL-WORK; Node: Windows / Codex Desktop / Codex / Lumi usage-link subtask.
// Original sources: current usage repair request, observed local quota schemas, synthetic regression fixtures.
using System.Text.Json;
using LumiCompanion.Infrastructure;

namespace LumiCompanion.Diagnostics;

public static class CodexUsageSelfTest
{
    public static void Run()
    {
        static void Check(bool ok, string message)
        {
            if (!ok) throw new InvalidOperationException("Codex usage: " + message);
        }
        var now = DateTimeOffset.Parse("2026-10-30T00:45:00+08:00");
        string root = Path.Combine(Path.GetTempPath(), "Lumi-usage-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var clock = new FixedClock(now);
            var service = new CodexUsageService(root, clock);
            void Identity(string account = "fixture-account", string user = "fixture-user") =>
                File.WriteAllText(Path.Combine(root, ".codex-global-state.json"), JsonSerializer.Serialize(new Dictionary<string, object>
                {
                    ["electron-persisted-atom-state"] = new Dictionary<string, object>
                    {
                        ["local-document-identity"] = new { accountId = account, accountUserId = user + "__" + account }
                    }
                }));
            string Record(DateTimeOffset at, double? five = 20, double? week = 30, string bucket = "codex",
                long? fiveReset = null, long? weekReset = null) => JsonSerializer.Serialize(new
            {
                timestamp = at.ToString("O"), type = "event_msg",
                payload = new
                {
                    type = "token_count", rate_limits = new
                    {
                        limit_id = bucket,
                        primary = five.HasValue ? new { used_percent = five, window_minutes = 300, resets_at = fiveReset ?? now.AddHours(1).ToUnixTimeSeconds() } : null,
                        secondary = week.HasValue ? new { used_percent = week, window_minutes = 10080, resets_at = weekReset ?? now.AddDays(3).ToUnixTimeSeconds() } : null
                    }
                }
            });
            void Session(string name, IEnumerable<string> lines, string account = "fixture-account", string user = "fixture-user", DateTimeOffset? folderTime = null)
            {
                DateTime date = (folderTime ?? now).LocalDateTime;
                string dir = Path.Combine(root, "sessions", date.ToString("yyyy"), date.ToString("MM"), date.ToString("dd"));
                Directory.CreateDirectory(dir);
                string metadata = JsonSerializer.Serialize(new { type = "session_meta", payload = new { creator_account_id = account, creator_user_id = user } });
                File.WriteAllLines(Path.Combine(dir, name + ".jsonl"), new[] { metadata }.Concat(lines));
            }
            void ClearSessions()
            {
                string sessions = Path.Combine(root, "sessions");
                if (Directory.Exists(sessions)) Directory.Delete(sessions, true);
            }
            Models.CodexUsageSnapshot Read() => service.ReadAsync(CancellationToken.None).GetAwaiter().GetResult();

            File.WriteAllText(Path.Combine(root, ".codex-global-state.json"), "{}");
            Check(!Read().HasAnyQuota, "missing identity must remain unknown");
            Identity();
            Session("matching", new[] { Record(now.AddMinutes(-2)) });
            var value = Read();
            Check(value.IsAvailable && value.FiveHour?.RemainingPercent == 80 && value.Weekly?.RemainingPercent == 70,
                "matching account reads both quota windows");
            Check(value.CapturedAt == now.AddMinutes(-2) && !value.IsStale && value.TodayTokens is null &&
                value.SourceLabel == "本地 Codex 限额快照", "preserve sample time and do not invent daily tokens");
            string? scope = value.AccountScopeKey;
            Check(scope is { Length: 64 } && !scope.Contains("fixture", StringComparison.Ordinal), "only hashed scope is exported");
            Session("different-user", new[] { Record(now, 99, 99) }, user: "other-user");
            Session("different-account", new[] { Record(now, 98, 98) }, account: "other-account");
            Check(Read().FiveHour?.UsedPercent == 20, "newer records from another user/account are rejected");
            Session("untrusted-content", new[] { Record(now, 97, 97).Replace("\"type\":\"event_msg\"", "\"type\":\"response_item\"", StringComparison.Ordinal) });
            Check(Read().FiveHour?.UsedPercent == 20, "quota-shaped conversation content is not an event");
            Session("other-bucket", new[] { Record(now, 97, 97, bucket: "codex_other") });
            Check(Read().FiveHour?.UsedPercent == 20, "different metered bucket is not a Codex quota");
            Session("partial", new[] { Record(now.AddMinutes(-1), five: 40, week: null), "{broken-tail" });
            value = Read();
            Check(value.HasAnyQuota && !value.IsAvailable && value.FiveHour?.RemainingPercent == 60 && value.Weekly is null,
                "newest partial snapshot is not merged with old secondary data");
            Check(value.BuildToolTip().Contains("本周额度未知", StringComparison.Ordinal), "missing window remains unknown");

            ClearSessions();
            Session("old", new[] { Record(now.AddMinutes(-11)) });
            value = Read();
            Check(value.IsStale && value.CapturedAt == now.AddMinutes(-11), "old snapshot remains explicitly stale");
            ClearSessions();
            Session("expired-reset", new[] { Record(now.AddMinutes(-1), fiveReset: now.AddSeconds(-1).ToUnixTimeSeconds()) });
            Check(Read().FiveHour is null && Read().Weekly is not null, "crossed reset invalidates only its own window");
            ClearSessions();
            Session("expired-age", new[] { Record(now.AddHours(-25)) }, folderTime: now.AddHours(-25));
            Check(!Read().HasAnyQuota, "snapshots older than 24 hours are not displayed");
            ClearSessions();
            Session("resumed-old-chat", new[] { Record(now.AddMinutes(-1)) }, folderTime: now.AddMonths(-2));
            Check(Read().CapturedAt == now.AddMinutes(-1) && Read().IsAvailable, "resumed old chat uses record time, not folder date");
            ClearSessions();
            Session("bounded-tail", new[] { new string(' ', 300 * 1024), Record(now.AddMinutes(-1)), Record(now.AddMinutes(-2), five: 90) });
            Check(Read().FiveHour?.UsedPercent == 20, "bounded tail selects newest event timestamp rather than last line");
            ClearSessions();
            Session("future", new[] { Record(now.AddMinutes(2)) });
            Check(!Read().HasAnyQuota, "future timestamps are rejected");
            ClearSessions();
            Session("decimal", new[] { Record(now, five: 20.2) });
            Check(Read().FiveHour?.RemainingPercent == 79, "fractional usage is not rounded into extra remaining quota");
            Identity(user: "switched-user");
            value = Read();
            Check(!value.HasAnyQuota && value.AccountScopeKey != scope, "account switch cannot retain prior account quota");

            using var mappedOther = JsonDocument.Parse("""
                {"result":{"rateLimitsByLimitId":{"other":{"primary":{"usedPercent":10,"windowDurationMins":300}}},
                "rateLimits":{"primary":{"usedPercent":80,"windowDurationMins":300}}}}
                """);
            Check(!CodexUsageService.ParseSnapshot(mappedOther.RootElement).HasAnyQuota, "explicit other-bucket map never falls back");
            using var swapped = JsonDocument.Parse("""
                {"result":{"rateLimits":{"primary":{"usedPercent":10.5,"windowDurationMins":10080},
                "secondary":{"usedPercent":40,"windowDurationMins":300}}}}
                """);
            value = CodexUsageService.ParseSnapshot(swapped.RootElement);
            Check(value.FiveHour?.RemainingPercent == 60 && value.Weekly?.RemainingPercent == 89,
                "window lengths determine five-hour/weekly roles even if primary and secondary swap");
            using var malformed = JsonDocument.Parse("""
                {"result":{"rateLimits":{"primary":{"usedPercent":"bad","windowDurationMins":300},
                "secondary":{"usedPercent":150,"windowDurationMins":10080}}}}
                """);
            Check(!CodexUsageService.ParseSnapshot(malformed.RootElement).HasAnyQuota, "malformed percentages do not become zero");
            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            Check(!service.ReadAsync(cancelled.Token).GetAwaiter().GetResult().HasAnyQuota, "cancelled read never returns quota");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now.ToUniversalTime();
    }
}
