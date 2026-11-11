// Version: 0.3.2-preview
// Generated/Updated/Coverage: 2026-10-30 (Asia/Shanghai)
// Source: LOCAL-WORK / Windows / Codex Desktop / Codex; original: local snapshot expiry requirements.
namespace LumiCompanion.Models;

public sealed record UsageBadgeProjection(CodexUsageWindow? FiveHour, CodexUsageWindow? Weekly, bool IsStale, string Status)
{
    public bool HasAnyQuota => FiveHour is not null || Weekly is not null;

    public static UsageBadgeProjection At(CodexUsageSnapshot snapshot, DateTimeOffset now)
    {
        if (!snapshot.HasAnyQuota) return new(null, null, snapshot.IsStale, snapshot.StatusMessage);
        TimeSpan age = now - snapshot.CapturedAt;
        if (age > TimeSpan.FromHours(24)) return new(null, null, true, "本地快照超过 24 小时，等待新数据。");
        CodexUsageWindow? Current(CodexUsageWindow? window) =>
            window?.ResetsAt is { } reset && reset <= now ? null : window;
        CodexUsageWindow? fiveHour = Current(snapshot.FiveHour);
        CodexUsageWindow? weekly = Current(snapshot.Weekly);
        bool expired = fiveHour != snapshot.FiveHour || weekly != snapshot.Weekly;
        string status = expired
            ? (fiveHour is null && weekly is null ? "额度窗口已到重置时间，等待新快照。" : "部分窗口已到重置时间，等待新快照。")
            : snapshot.StatusMessage;
        return new(fiveHour, weekly, snapshot.IsStale || age > TimeSpan.FromMinutes(10), status);
    }
}
