// Version: 0.3.14-preview. Generated: 2026-10-30; updated/coverage: 2026-11-11 (Asia/Shanghai).
// Source: RAW | user notification and dismissal rules; LOCAL-WORK | Codex Desktop / Lumi / Codex.
using LumiCompanion.Behavior;

namespace LumiCompanion.Integration;

/// <summary>Applies observations for one source without celebrating historical startup records.</summary>
public sealed class LocalTaskStatusCoordinator(BehaviorController behavior, TimeProvider? timeProvider = null,
    string source = LocalCodexTaskSource.SourceId)
{
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;
    private string? _scope;
    private DateTimeOffset _connectedAt;
    private readonly HashSet<string> _seenTasks = new(StringComparer.Ordinal);
    private DateTimeOffset? _latestBridgeUpdate;

    public void Apply(LocalCodexTaskReadResult result)
    {
        if (source != LocalCodexTaskSource.SourceId) return;
        if (!result.SourceAvailable || string.IsNullOrWhiteSpace(result.AccountScopeKey))
        {
            Disconnect();
            return;
        }

        bool baseline = BeginScope(result.AccountScopeKey);
        foreach (ObservedCompletionEvent completion in result.CompletionEvents)
        {
            if (completion.Source != source) continue;
            bool announce = !baseline && completion.EventAt >= _connectedAt && !AlreadyConfirmed(completion.EventAt);
            behavior.ObserveTaskCompletion(completion, announce);
        }
        foreach (LocalCodexTaskObservation observation in result.Observations)
            ApplyObservation(observation.Snapshot, observation.StateVersion, observation.EventAt,
                observation.TurnId, observation.Snapshot.TaskId, baseline);
    }

    public void Apply(WorkBuddyTaskReadResult result)
    {
        if (source != LocalWorkBuddyTaskSource.SourceId) return;
        if (result.Snapshot is not { } snapshot || snapshot.Source != source ||
            string.IsNullOrWhiteSpace(result.ScopeKey) || string.IsNullOrWhiteSpace(snapshot.TaskId) ||
            result.UpdatedAt is not { } updatedAt || updatedAt > snapshot.ObservedAt)
        {
            Disconnect();
            return;
        }

        bool baseline = BeginScope(result.ScopeKey);
        if (_latestBridgeUpdate is { } latest && updatedAt < latest) return;
        _latestBridgeUpdate = updatedAt;

        // The bridge publishes one current workspace task. Each session/turn is a distinct result.
        string turn = snapshot.TaskId;
        ApplyObservation(snapshot with { TaskId = result.ScopeKey }, null, updatedAt, turn, turn, baseline);
    }

    private void ApplyObservation(AgentTaskSnapshot snapshot, string? version, DateTimeOffset eventAt, string? turnId,
        string seenIdentity, bool baseline)
    {
        if (snapshot.Source != source) return;
        bool announce = !baseline && (_seenTasks.Contains(seenIdentity) || eventAt >= _connectedAt);
        // The bridge can deliver a terminal event after the user has already confirmed it.
        // Compare the source's event time with the interaction, not the polling time.
        if (snapshot.State is AgentTaskState.Completed or AgentTaskState.Failed && AlreadyConfirmed(eventAt))
            announce = false;
        behavior.ObserveTaskState(snapshot, version, announceTerminal: announce, turnId: turnId);
        _seenTasks.Add(seenIdentity);
    }

    private bool AlreadyConfirmed(DateTimeOffset eventAt) =>
        behavior.LastAcknowledgementAt is { } acknowledgedAt && eventAt <= acknowledgedAt;

    private bool BeginScope(string scope)
    {
        if (_scope == scope) return false;
        Disconnect();
        _scope = scope;
        _connectedAt = _time.GetUtcNow();
        return true;
    }

    public void Disconnect()
    {
        behavior.DisconnectTaskSource(source);
        _scope = null;
        _seenTasks.Clear();
        _latestBridgeUpdate = null;
    }
}
