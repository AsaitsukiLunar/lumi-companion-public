// Version: 0.3.21-preview | Generated: 2026-10-08 | Updated/Coverage: 2026-11-17 (Asia/Shanghai)
// Source: RAW | user current-batch counts, acknowledgement rules and agent separation; LOCAL-WORK | Codex Desktop / Lumi / Codex
namespace LumiCompanion.Integration;

public enum AgentTaskState { Unknown, Idle, Completed, Review, Running, Failed, Waiting }
public sealed record AgentTaskSnapshot(string Source, string TaskId, AgentTaskState State,
    DateTimeOffset ObservedAt, DateTimeOffset ExpiresAt);
public sealed record AggregateTaskState(AgentTaskState State, int RunningCount, int WaitingCount,
    int KnownCount, bool HasUnknown)
{
    // Counts do not change animation arbitration. The behavior layer supplies the bounded current batch.
    public ConversationProgress? Progress { get; init; }
    public ConversationProgress? WorkBuddyProgress { get; init; }
    public IReadOnlyDictionary<string, ConversationProgress>? ProgressBySource { get; init; }
}

public sealed record ConversationProgress(int TotalCount, int RunningCount, int ReviewCount,
    int CompletedCount, int WaitingCount, int FailedCount, int UnknownCount, int InactiveCount,
    bool IsCurrentBatch = false)
{
    public int ProcessingCount => RunningCount + ReviewCount;
    // Processing counts live chats; terminal counts are distinct turn results. TotalCount
    // is their combined number of entries, never a claim about unique chats or all accounts.
    public bool AllCompleted => IsCurrentBatch && CompletedCount > 0 && ProcessingCount == 0 &&
        WaitingCount == 0 && FailedCount == 0 && UnknownCount == 0 && InactiveCount == 0;

    internal static ConversationProgress FromStates(IEnumerable<AgentTaskState> observations, bool currentBatch = false)
    {
        AgentTaskState[] states = observations.ToArray();
        return new(states.Length, states.Count(state => state == AgentTaskState.Running),
            states.Count(state => state == AgentTaskState.Review), states.Count(state => state == AgentTaskState.Completed),
            states.Count(state => state == AgentTaskState.Waiting), states.Count(state => state == AgentTaskState.Failed),
            states.Count(state => state == AgentTaskState.Unknown), states.Count(state => state == AgentTaskState.Idle), currentBatch);
    }
}

/// <summary>Consumes read-only status snapshots; never starts or controls tasks.</summary>
public sealed class TaskStateArbiter
{
    private readonly Dictionary<(string Source, string TaskId), AgentTaskSnapshot> _tasks = new();
    public bool Observe(AgentTaskSnapshot task, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(task.Source) || string.IsNullOrWhiteSpace(task.TaskId) ||
            task.ExpiresAt <= now || task.ObservedAt > now || task.ExpiresAt <= task.ObservedAt)
            return false;
        var key = (task.Source, task.TaskId);
        if (_tasks.TryGetValue(key, out var previous) && task.ObservedAt <= previous.ObservedAt)
            return false;
        _tasks[key] = task;
        return true;
    }

    public AggregateTaskState Read(DateTimeOffset now)
    {
        foreach (var key in _tasks.Where(p => p.Value.ExpiresAt <= now).Select(p => p.Key).ToArray())
            _tasks.Remove(key);
        var states = _tasks.Values.ToArray();
        int known = states.Count(t => t.State != AgentTaskState.Unknown);
        AgentTaskState selected = states.Where(t => t.State != AgentTaskState.Unknown)
            .OrderByDescending(t => Priority(t.State)).ThenByDescending(t => t.ObservedAt)
            .Select(t => t.State).FirstOrDefault(AgentTaskState.Unknown);
        return new(selected, states.Count(t => t.State == AgentTaskState.Running),
            states.Count(t => t.State == AgentTaskState.Waiting), known,
            states.Length == 0 || states.Any(t => t.State == AgentTaskState.Unknown))
        { Progress = ConversationProgress.FromStates(states.Select(task => task.State)) };
    }

    public void Disconnect(string source)
    {
        foreach (var key in _tasks.Keys.Where(k => k.Source == source).ToArray()) _tasks.Remove(key);
    }
    private static int Priority(AgentTaskState state) => state switch
    {
        // Live work resumes immediately even while an older failed turn awaits confirmation.
        // Waiting for the user keeps its existing priority; terminal counts are independent.
        AgentTaskState.Running => 55,
        AgentTaskState.Waiting => 60, AgentTaskState.Failed => 50,
        AgentTaskState.Review => 30,
        AgentTaskState.Completed => 20, AgentTaskState.Idle => 10, _ => 0
    };
}
