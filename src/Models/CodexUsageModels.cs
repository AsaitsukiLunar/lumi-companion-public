/*
Traceability metadata
Version: 0.3.2-preview
Generated: 2026-10-02 (Asia/Shanghai)
Last updated: 2026-11-10
Content coverage through: 2026-11-10
Source type: LOCAL-WORK; RAW (user quota reset time display request)
Source: Codex Desktop / workspace new-chat / Lumi Codex usage display / Codex
*/

namespace LumiCompanion.Models;

public sealed record CodexUsageWindow(
    int WindowDurationMinutes,
    int UsedPercent,
    DateTimeOffset? ResetsAt)
{
    public int RemainingPercent => Math.Clamp(100 - UsedPercent, 0, 100);
}

public sealed record CodexUsageSnapshot(
    CodexUsageWindow? FiveHour,
    CodexUsageWindow? Weekly,
    DateTimeOffset CapturedAt,
    string StatusMessage)
{
    public bool IsAvailable => FiveHour is not null && Weekly is not null;
    public bool HasAnyQuota => FiveHour is not null || Weekly is not null;
    public string SourceLabel { get; init; } = "Codex 用量协议";
    public bool IsStale { get; init; }
    public string? AccountScopeKey { get; init; }
    public long? TodayTokens { get; init; }
    public string TodayStatus { get; init; } = "今日 Token 未返回";

    public static CodexUsageSnapshot Loading() =>
        new(null, null, DateTimeOffset.Now, "正在读取 Codex 用量…");

    public static CodexUsageSnapshot Unavailable(string message) =>
        new(null, null, DateTimeOffset.Now, message);

    public string BuildToolTip(bool showResetTimes = true)
    {
        if (!HasAnyQuota)
        {
            return SourceLabel + Environment.NewLine + StatusMessage;
        }

        string QuotaLine(string label, CodexUsageWindow? window)
        {
            if (window is null) return $"{label}额度未知";
            string remaining = $"{label}剩余 {window.RemainingPercent}%";
            return showResetTimes ? $"{remaining} · {FormatReset(window.ResetsAt)}" : remaining;
        }

        return string.Join(Environment.NewLine,
            QuotaLine("5 小时", FiveHour),
            QuotaLine("本周", Weekly),
            TodayTokens.HasValue ? $"今日账号 Token {TodayTokens:N0}" : TodayStatus,
            $"{SourceLabel} · {CapturedAt.LocalDateTime:MM-dd HH:mm} 采样" + (IsStale ? " · 旧快照" : ""),
            StatusMessage);
    }

    private static string FormatReset(DateTimeOffset? value) =>
        value.HasValue ? $"{value.Value.LocalDateTime:MM-dd HH:mm} 重置" : "重置时间未知";
}
