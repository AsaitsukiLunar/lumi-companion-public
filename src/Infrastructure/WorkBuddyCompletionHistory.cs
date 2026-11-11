/*
Version: 0.3.10-preview
Generated / updated / coverage: 2026-11-11 (Asia/Shanghai)
Source type: RAW | user WorkBuddy completion history request; LOCAL-WORK | implementation
Source: Codex Desktop / lumi-companion / current Lumi conversation (title unverified)
Organizing node: Windows / Codex Desktop / Codex
Original sources: WorkBuddy's verified completion events and Lumi's existing completion snapshot model.
*/
using System.Security.Cryptography;
using System.Text.Json;
using LumiCompanion.Integration;
using LumiCompanion.Models;

namespace LumiCompanion.Infrastructure;

/// <summary>Projects completed WorkBuddy turns from the current bridge log without another ledger.</summary>
public sealed class WorkBuddyCompletionHistory
{
    private readonly TimeProvider _time;
    private readonly DateTimeOffset _launchedAt;
    private IReadOnlyList<ObservedCompletionEvent>? _appliedEvents;
    private CompletionHistoryEntry[]? _entries;
    private string? _scopeHash;
    private bool _enabled;
    private bool _paused;

    public WorkBuddyCompletionHistory(bool enabled, TimeProvider? timeProvider = null)
    {
        _time = timeProvider ?? TimeProvider.System;
        _launchedAt = _time.GetUtcNow();
        _enabled = enabled;
    }

    public void SetEnabled(bool enabled)
    {
        _enabled = enabled;
        if (!enabled) SetSourceUnavailable();
    }

    public void SetObservationPaused(bool paused)
    {
        _paused = paused;
        if (paused) SetSourceUnavailable();
    }

    public void SetSourceUnavailable()
    {
        _appliedEvents = null;
        _entries = null;
        _scopeHash = null;
    }

    /// <summary>Consumes one immutable, complete snapshot of the current source log.</summary>
    public void Apply(string scopeKey, IReadOnlyList<ObservedCompletionEvent> events)
    {
        if (!_enabled || _paused || string.IsNullOrWhiteSpace(scopeKey))
        {
            SetSourceUnavailable();
            return;
        }

        const string source = LocalWorkBuddyTaskSource.SourceId;
        string scopeHash = Hash([source, scopeKey]);
        if (_scopeHash == scopeHash && ReferenceEquals(_appliedEvents, events)) return;

        DateTimeOffset now = _time.GetUtcNow();
        var completed = new Dictionary<(string Task, string Turn), DateTimeOffset>();
        bool containsFutureEvent = false;
        foreach (ObservedCompletionEvent completion in events)
        {
            if (completion.Source != source || string.IsNullOrWhiteSpace(completion.TaskId) ||
                string.IsNullOrWhiteSpace(completion.TurnId) || string.IsNullOrWhiteSpace(completion.StateVersion) ||
                completion.EventAt == default) continue;
            if (completion.EventAt > now)
            {
                containsFutureEvent = true;
                continue;
            }

            var key = (completion.TaskId, completion.TurnId);
            if (!completed.TryGetValue(key, out DateTimeOffset existing) || completion.EventAt < existing)
                completed[key] = completion.EventAt;
        }

        _entries = completed.Select(pair => new CompletionHistoryEntry(scopeHash,
            Hash([source, scopeHash, pair.Key.Task, pair.Key.Turn]), source, pair.Value.ToUniversalTime()))
            .OrderByDescending(entry => entry.EventAt).ToArray();
        _scopeHash = scopeHash;
        // Revisit future-dated records on the next poll, even when the log has not changed.
        _appliedEvents = containsFutureEvent ? null : events;
    }

    public CompletionHistorySnapshot Read()
    {
        if (!_enabled) return Unavailable("本地统计已关闭。");
        if (_paused) return Unavailable("任务感知已暂停。");
        if (_entries is null) return Unavailable("WorkBuddy 完成事件暂不可用。");

        DateTimeOffset now = _time.GetUtcNow();
        TimeZoneInfo zone = _time.LocalTimeZone;
        DateTime today = TimeZoneInfo.ConvertTime(now, zone).Date;
        int total = 0, todayCount = 0, thisLaunch = 0;
        var recent = new List<CompletionHistoryEntry>(10);
        foreach (CompletionHistoryEntry entry in _entries)
        {
            if (entry.EventAt > now) continue;
            total++;
            if (entry.EventAt > _launchedAt) thisLaunch++;
            if (TimeZoneInfo.ConvertTime(entry.EventAt, zone).Date == today) todayCount++;
            if (recent.Count < 10) recent.Add(entry);
        }

        return new(true, true, "统计 WorkBuddy 事件文件中的已完成回合。", thisLaunch, todayCount, total,
            recent, LocalWorkBuddyTaskSource.SourceId);
    }

    private CompletionHistorySnapshot Unavailable(string status) => new(_enabled, false, status, null, null, null, []);

    private static string Hash(string[] values) => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(values)));
}
