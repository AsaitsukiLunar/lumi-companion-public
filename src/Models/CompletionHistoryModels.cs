// Version: 0.3.7-preview; Generated/Updated/Coverage: 2026-10-30 (Asia/Shanghai)
// Source: RAW | user persistent completion counts; LOCAL-WORK | Windows / Codex Desktop / completion_ledger.
namespace LumiCompanion.Models;

public sealed record CompletionHistoryEntry(string ScopeHash, string EventKey, string Source, DateTimeOffset EventAt);

public sealed record CompletionHistorySnapshot(bool Enabled, bool Available, string Status,
    int? ThisLaunch, int? Today, int? Total, IReadOnlyList<CompletionHistoryEntry> Recent,
    string? ConnectedSource = null);
