/*
Traceability metadata
Version: 1.2
Generated / Last updated / Content coverage through: 2026-11-11 (Asia/Shanghai)
Source type: LOCAL-WORK | WorkBuddy bridge adapter regression checks
Organizing node: Windows / Codex Desktop / Codex / Lumi independent companion
Original sources: WorkBuddy bridge handoff V1.0 and synthetic protocol fixtures
*/

using System.Text.Json;
using LumiCompanion.Integration;

namespace LumiCompanion.Diagnostics;

public static class LocalWorkBuddyTaskSourceSelfTest
{
    public static void Run()
    {
        string temporary = Path.Combine(Path.GetTempPath(), "Lumi-workbuddy-bridge-test-" + Guid.NewGuid().ToString("N"));
        const string workspace = "示例工作区";
        const string otherWorkspace = "second-workspace";
        string directory = Path.Combine(temporary, workspace, ".workbuddy", "lumi");
        string otherDirectory = Path.Combine(temporary, otherWorkspace, ".workbuddy", "lumi");
        Directory.CreateDirectory(directory);
        var clock = new TestClock();
        var source = new LocalWorkBuddyTaskSource(directory, clock);
        var discovered = new LocalWorkBuddyTaskSource(null, clock, temporary);
        string statePath = Path.Combine(directory, "wb-state.json");
        string eventsPath = Path.Combine(directory, "wb-events.jsonl");
        string otherStatePath = Path.Combine(otherDirectory, "wb-state.json");

        WorkBuddyTaskReadResult Read() => source.ReadAsync(CancellationToken.None).GetAwaiter().GetResult();
        WorkBuddyTaskReadResult ReadDiscovered() => discovered.ReadAsync(CancellationToken.None).GetAwaiter().GetResult();
        static void Check(bool result, string message) { if (!result) throw new InvalidOperationException(message); }

        string Row(string state, bool snapshot, params (string Name, object? Value)[] overrides)
        {
            var fields = new Dictionary<string, object?>
            {
                ["schema"] = "wb-lumi-bridge/0.1",
                ["sessionId"] = "session-a",
                ["turnId"] = "turn-one",
                ["turnSeq"] = 1,
                ["state"] = state,
                [snapshot ? "updatedAt" : "at"] = clock.GetUtcNow().ToUnixTimeMilliseconds()
            };
            if (snapshot)
            {
                fields["source"] = "workbuddy-desktop";
                fields["workspace"] = workspace;
                fields["leaseMs"] = 900_000;
                fields["note"] = "synthetic-note-must-not-appear";
            }
            foreach (var (name, value) in overrides) fields[name] = value;
            return JsonSerializer.Serialize(fields);
        }

        void Snapshot(string state, params (string Name, object? Value)[] overrides) =>
            File.WriteAllText(statePath, Row(state, true, overrides));

        try
        {
            Check(Read() is { Snapshot: null, ScopeKey: null, CompletionEventsAvailable: false },
                "missing bridge files are unavailable");
            Check(ReadDiscovered() is { Snapshot: null, ScopeKey: null },
                "auto-discovery stays disconnected until a bridge file is published");

            DateTimeOffset originalUpdate = clock.GetUtcNow().AddSeconds(-7);
            Snapshot("running", ("updatedAt", originalUpdate.ToUnixTimeMilliseconds()));
            var running = Read();
            Check(running is { Snapshot: { Source: LocalWorkBuddyTaskSource.SourceId,
                TaskId: "session-a/turn-one", State: AgentTaskState.Running },
                BridgeState: "running", TurnSeq: 1, CompletionEventsAvailable: false, ScopeKey.Length: 64 },
                "verified running snapshot has local workspace scope and stable turn identity");
            Check(running.UpdatedAt == originalUpdate && running.Snapshot?.ObservedAt == clock.GetUtcNow(),
                "original bridge timestamp is preserved separately from polling time");
            Check(!JsonSerializer.Serialize(running).Contains("synthetic-note-must-not-appear", StringComparison.Ordinal),
                "the snapshot note is absent from the adapter result");
            Check(ReadDiscovered().ScopeKey == running.ScopeKey,
                "exactly one published workspace is discovered with the same scope as an explicit path");
            Directory.CreateDirectory(otherDirectory);
            File.WriteAllText(otherStatePath, Row("running", true, ("workspace", otherWorkspace)));
            Check(ReadDiscovered() is { Snapshot: null, ScopeKey: null } && Read().ScopeKey == running.ScopeKey,
                "multiple bridges need an explicit directory, which selects only that workspace");
            File.Delete(otherStatePath);
            Check(ReadDiscovered().ScopeKey == running.ScopeKey,
                "auto-discovery reconnects when just one published bridge is available again");

            Snapshot("running", ("sessionId", "session-b"), ("turnSeq", 2));
            var changedSession = Read();
            Check(changedSession.ScopeKey == running.ScopeKey && changedSession.TurnSeq == 2 &&
                changedSession.Snapshot?.TaskId == "session-b/turn-one", "session changes preserve workspace scope");
            clock.Advance(TimeSpan.FromMinutes(15));
            Check(Read() is { Snapshot.State: AgentTaskState.Unknown, BridgeState: "expired", CompletionEvents.Count: 0 },
                "an expired running lease is unknown without creating a completion");

            Snapshot("waiting_user");
            Check(Read() is { Snapshot.State: AgentTaskState.Waiting, BridgeState: "waiting_user" },
                "waiting for user input maps to Waiting");
            clock.Advance(TimeSpan.FromMinutes(15));
            Check(Read() is { Snapshot.State: AgentTaskState.Waiting, BridgeState: "waiting_user" },
                "the bridge's running lease does not expire waiting for user input");
            Snapshot("cancelled");
            Check(Read() is { Snapshot.State: AgentTaskState.Idle, BridgeState: "cancelled" }, "cancellation is neutral");
            Snapshot("failed");
            Check(Read() is { Snapshot.State: AgentTaskState.Failed, BridgeState: "failed" }, "failure has its own state");

            Snapshot("completed");
            File.WriteAllText(eventsPath, "");
            Check(Read() is { Snapshot.State: AgentTaskState.Completed, CompletionEventsAvailable: true, CompletionEvents.Count: 0 },
                "a completed snapshot is display evidence and an empty journal is available");

            DateTimeOffset eventAt = clock.GetUtcNow().AddMinutes(-2);
            string partial = Row("completed", false, ("turnId", "partial"));
            string journal = string.Join("\n",
                Row("completed", false, ("at", eventAt.ToUnixTimeMilliseconds())),
                "{malformed",
                Row("completed", false),
                Row("completed", false, ("sessionId", "session-b")),
                Row("failed", false, ("turnId", "failed")),
                Row("cancelled", false, ("turnId", "cancelled")),
                Row("completed", false, ("turnId", "wrong-source"), ("source", "other")),
                Row("completed", false, ("turnId", "wrong-workspace"), ("workspace", "other")),
                Row("completed", false, ("turnId", "wrong-schema"), ("schema", "unknown")),
                Row("completed", false, ("turnId", "invalid-utf")).Replace("invalid-utf", "\\uD800", StringComparison.Ordinal),
                Row("completed", false, ("turnId", "path/segment")),
                Row("completed", false, ("turnId", "wrong-seq"), ("turnSeq", 0)),
                Row("completed", false, ("turnId", "future"), ("at", clock.GetUtcNow().AddSeconds(1).ToUnixTimeMilliseconds())),
                partial);
            File.WriteAllText(eventsPath, journal);
            var replay = Read();
            Check(replay.CompletionEventsAvailable && replay.CompletionEvents.Count == 2 &&
                replay.CompletionEvents.Single(item => item.TaskId == "session-a/turn-one").EventAt == eventAt &&
                replay.CompletionEvents.Any(item => item.TaskId == "session-b/turn-one"),
                "valid terminal rows deduplicate by session and turn and preserve the original event time");

            File.AppendAllText(eventsPath, "\n");
            Check(Read().CompletionEvents.Count == 3, "an appended final row becomes visible once its newline arrives");

            Snapshot("running", ("workspace", "other"));
            Check(Read() is { Snapshot: null, CompletionEventsAvailable: true, CompletionEvents.Count: 3 },
                "invalid snapshot identity does not prevent valid event replay");
            File.Delete(statePath);
            Check(Read().ScopeKey == running.ScopeKey && Read().Snapshot is null,
                "event-only replay uses the same configured workspace scope");

            Snapshot("running", ("leaseMs", 0));
            Check(Read().Snapshot is null, "zero lease is invalid");
            Snapshot("running", ("updatedAt", clock.GetUtcNow().AddSeconds(1).ToUnixTimeMilliseconds()));
            Check(Read().Snapshot is null, "future snapshot time is invalid");

            Snapshot("completed");
            File.WriteAllText(eventsPath, "{malformed\n");
            Check(Read() is { Snapshot.State: AgentTaskState.Completed, CompletionEventsAvailable: false, CompletionEvents.Count: 0 },
                "a corrupt journal is unavailable while a valid snapshot still displays");
            File.WriteAllText(eventsPath, Row("failed", false) + "\n");
            Check(Read() is { CompletionEventsAvailable: true, CompletionEvents.Count: 0 },
                "truncation rereads complete terminal rows without retaining stale completions");

            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            bool cancelledRead = false;
            try { source.ReadAsync(cancelled.Token).GetAwaiter().GetResult(); }
            catch (OperationCanceledException) { cancelledRead = true; }
            Check(cancelledRead, "cancellation is honored");
        }
        finally
        {
            DeleteBridge(otherDirectory);
            DeleteBridge(directory);
            Directory.Delete(temporary);
        }
    }

    private static void DeleteBridge(string directory)
    {
        if (!Directory.Exists(directory)) return;
        File.Delete(Path.Combine(directory, "wb-state.json"));
        File.Delete(Path.Combine(directory, "wb-events.jsonl"));
        Directory.Delete(directory);
        string stateRoot = Path.GetDirectoryName(directory)!;
        Directory.Delete(stateRoot);
        Directory.Delete(Path.GetDirectoryName(stateRoot)!);
    }

    private sealed class TestClock : TimeProvider
    {
        private DateTimeOffset _now = DateTimeOffset.Parse("2026-11-11T13:47:15+08:00");
        public override DateTimeOffset GetUtcNow() => _now.ToUniversalTime();
        public void Advance(TimeSpan elapsed) => _now += elapsed;
    }
}
