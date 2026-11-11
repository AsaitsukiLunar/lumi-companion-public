// Version: 0.3.7-preview | Generated: 2026-10-08 | Updated/Coverage: 2026-10-31 (Asia/Shanghai)
// Source: LOCAL-WORK / Codex Desktop / current Lumi conversation / Codex
using System.Text.Json;
using LumiCompanion.Integration;
namespace LumiCompanion.Diagnostics;
public static class IntegrationSelfTest
{
    public static void Run()
    {
        static void Check(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }
        var now = DateTimeOffset.Parse("2026-10-08T22:00:00+08:00");
        var arbiter = new TaskStateArbiter();
        AgentTaskSnapshot Task(string id, AgentTaskState state, int offset = 0) =>
            new("codex", id, state, now.AddSeconds(offset), now.AddMinutes(5));
        Check(arbiter.Read(now).State == AgentTaskState.Unknown, "missing source is unknown");
        Check(arbiter.Observe(Task("a", AgentTaskState.Running), now), "first task");
        Check(arbiter.Observe(Task("b", AgentTaskState.Running), now), "second task");
        Check(arbiter.Read(now).RunningCount == 2, "parallel tasks");
        Check(!arbiter.Observe(Task("a", AgentTaskState.Idle), now), "duplicate timestamp ignored");
        Check(arbiter.Observe(Task("a", AgentTaskState.Completed, 1), now.AddSeconds(1)), "completion");
        Check(arbiter.Read(now.AddSeconds(1)).State == AgentTaskState.Running, "completion cannot replace active task");
        Check(arbiter.Read(now.AddSeconds(1)).Progress is { ProcessingCount: 1, CompletedCount: 1, IsCurrentBatch: false },
            "raw aggregate exposes distinct current states without inventing a batch-wide success claim");
        arbiter.Observe(Task("failed", AgentTaskState.Failed, 1), now.AddSeconds(1));
        Check(arbiter.Read(now.AddSeconds(1)) is { State: AgentTaskState.Running, RunningCount: 1,
            Progress: { CompletedCount: 1, FailedCount: 1 } },
            "failure cannot replace active work or erase completed and failed counts");
        arbiter.Observe(Task("c", AgentTaskState.Waiting, 1), now.AddSeconds(1));
        Check(arbiter.Read(now.AddSeconds(1)).State == AgentTaskState.Waiting, "waiting priority");
        Check(!arbiter.Observe(Task("b", AgentTaskState.Failed, -1), now), "stale update ignored");
        Check(arbiter.Read(now.AddMinutes(6)).State == AgentTaskState.Unknown, "expired state unknown");
        arbiter.Observe(Task("a", AgentTaskState.Running), now);
        arbiter.Disconnect("codex");
        Check(arbiter.Read(now).HasUnknown, "disconnect clears stale work");
        arbiter.Observe(Task("review", AgentTaskState.Review), now);
        arbiter.Observe(Task("failed", AgentTaskState.Failed), now);
        Check(arbiter.Read(now).State == AgentTaskState.Failed,
            "the Running correction preserves existing Review versus Failed arbitration");
        using var notification = JsonDocument.Parse("{\"method\":\"thread/status/changed\",\"params\":{\"threadId\":\"demo\",\"status\":{\"type\":\"active\",\"activeFlags\":[\"waitingOnApproval\"]}}}");
        Check(CodexStatusDecoder.Decode(notification.RootElement, now, TimeSpan.FromMinutes(5))?.State == AgentTaskState.Waiting, "approval decoder");
        using var malformed = JsonDocument.Parse("{\"method\":\"thread/status/changed\",\"params\":{\"threadId\":42}}");
        Check(CodexStatusDecoder.Decode(malformed.RootElement, now, TimeSpan.FromSeconds(5)) is null, "malformed rejected");
    }
}
