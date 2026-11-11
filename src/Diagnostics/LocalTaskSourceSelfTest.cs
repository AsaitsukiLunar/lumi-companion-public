/*
Version: 0.3.8-preview
Generated: 2026-10-30 (Asia/Shanghai)
Last updated / Content coverage through: 2026-11-10 (Asia/Shanghai)
Source type: LOCAL-WORK | Codex local task-source regression checks
Organizing node: Windows / Codex Desktop / Codex / github_reuse subtask
Original source: synthetic fixtures matching observed event schemas, never real conversation text or credentials.
*/
using System.Text;
using System.Text.Json;
using LumiCompanion.Integration;

namespace LumiCompanion.Diagnostics;

public static class LocalTaskSourceSelfTest
{
    public static void Run()
    {
        string temporary = Path.Combine(Path.GetTempPath(), "Lumi-local-task-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(temporary, "sessions"));
        var clock = new TestClock();
        var source = new LocalCodexTaskSource(temporary, clock);
        LocalCodexTaskReadResult Read() => source.ReadAsync(CancellationToken.None).GetAwaiter().GetResult();
        static void Check(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }
        void Identity(string account = "test-account", string user = "test-user") =>
            File.WriteAllText(Path.Combine(temporary, ".codex-global-state.json"), JsonSerializer.Serialize(new Dictionary<string, object>
            {
                ["electron-persisted-atom-state"] = new Dictionary<string, object>
                {
                    ["local-document-identity"] = new { accountId = account, accountUserId = user + "__" + account }
                }
            }));
        string Session(string id, string account = "test-account", object? sourceKind = null, string originator = "Codex Desktop")
        {
            string path = Path.Combine(temporary, "sessions", id + ".jsonl");
            File.WriteAllText(path, JsonSerializer.Serialize(new
            {
                type = "session_meta",
                payload = new { id, creator_account_id = account, creator_user_id = "test-user", originator, source = sourceKind ?? "vscode" }
            }) + "\n", new UTF8Encoding(false));
            return path;
        }
        void Event(string path, string type, string turn, object? error = null, string? itemType = null,
            DateTimeOffset? eventAt = null)
        {
            var payload = new Dictionary<string, object?> { ["type"] = type, ["turn_id"] = turn };
            if (error is not null) payload["error"] = error;
            if (itemType is not null) payload["item"] = new { type = itemType };
            File.AppendAllText(path, JsonSerializer.Serialize(new
            {
                timestamp = (eventAt ?? clock.GetUtcNow()).ToString("O"), type = "event_msg", payload
            }) + "\n", new UTF8Encoding(false));
        }
        void PushLifecycleOutsideTail(string path) =>
            File.AppendAllText(path, JsonSerializer.Serialize(new
            {
                type = "synthetic_padding", payload = new string('x', LocalCodexTaskSource.TailBytes + 1024)
            }) + "\n", new UTF8Encoding(false));
        try
        {
            Check(!Read().SourceAvailable, "missing desktop identity must not produce state");
            Identity();
            Check(Read().SourceAvailable && Read().Observations.Count == 0, "available identity with no chats differs from unavailable source");
            Directory.Delete(Path.Combine(temporary, "sessions"));
            Check(!Read().SourceAvailable && Read().Observations.Count == 0,
                "missing session directory is an unavailable source, not a successful empty observation");
            Directory.CreateDirectory(Path.Combine(temporary, "sessions"));
            Check(Read().SourceAvailable, "restored empty session directory is available again");
            string main = Session("main-chat");
            Event(main, "task_started", "turn-one");
            var started = Read();
            Check(started.MatchedFiles == 1 && started.Observations.Single().Snapshot.State == AgentTaskState.Running &&
                  started.Observations.Single().TurnId == "turn-one",
                "verified desktop task start is Running");
            var original = started.Observations.Single();
            clock.Advance(TimeSpan.FromSeconds(2));
            var renewed = Read().Observations.Single();
            Check(renewed.StateVersion == original.StateVersion && renewed.Snapshot.ObservedAt > original.Snapshot.ObservedAt,
                "unchanged file renews the display lease with a stable event version");
            clock.Advance(TimeSpan.FromMinutes(4));
            Event(main, "item_completed", "turn-one", itemType: "CommandExecution");
            clock.Advance(TimeSpan.FromMinutes(2));
            Check(Read().Observations.Single().Snapshot.State == AgentTaskState.Running,
                "same-turn item activity renews activity freshness without fabricating a new task");
            clock.Advance(TimeSpan.FromMinutes(4));
            Check(Read().Observations.Single().Snapshot.State == AgentTaskState.Unknown,
                "no task activity beyond the freshness window becomes Unknown");
            Event(main, "task_complete", "turn-one");
            var completed = Read().Observations.Single();
            Check(completed.Snapshot.State == AgentTaskState.Completed && completed.StateVersion != original.StateVersion &&
                  completed.TurnId == "turn-one",
                "normal task_complete is an explicit turn ending with a new stable version");
            Event(main, "task_started", "turn-two");
            Check(Read().Observations.Count == 1 && Read().Observations.Single().Snapshot.State == AgentTaskState.Running,
                "a chat's new turn replaces its old terminal state rather than adding another task");
            Event(main, "task_complete", "turn-two", error: new { message = "synthetic error", codex_error_info = new { type = "test" } });
            Check(Read().Observations.Single().Snapshot.State == AgentTaskState.Failed, "explicit terminal error is Failed");
            Event(main, "task_complete", "turn-two", error: new { });
            Check(Read().Observations.Single().Snapshot.State == AgentTaskState.Unknown,
                "empty error object is Unknown rather than a fabricated failure");
            Event(main, "task_complete", "turn-two", error: new { unrelated = "value" });
            Check(Read().Observations.Single().Snapshot.State == AgentTaskState.Unknown,
                "unknown error object schema is not proof of failure");
            Event(main, "task_complete", "turn-two", error: false);
            Check(Read().Observations.Single().Snapshot.State == AgentTaskState.Unknown,
                "unexpected error boolean is not proof of failure or success");
            Event(main, "task_complete", "turn-two", error: new { codex_error_info = new { type = "synthetic_explicit_failure" } });
            Check(Read().Observations.Single().Snapshot.State == AgentTaskState.Failed,
                "explicit tagged codex error remains Failed without a message");
            Event(main, "turn_aborted", "turn-three");
            Check(Read().Observations.Single().Snapshot.State == AgentTaskState.Idle, "cancelled turn is neutral, never failure or success");

            string otherAccount = Session("other-account", "different-account");
            Event(otherAccount, "task_started", "other-turn");
            string cli = Session("cli-chat", sourceKind: "cli", originator: "codex_cli_rs");
            Event(cli, "task_started", "cli-turn");
            string child = Session("subagent-chat", sourceKind: new { subagent = new { parent_thread_id = "synthetic-parent" } });
            Event(child, "task_started", "child-turn");
            Check(Read().MatchedFiles == 1 && Read().Observations.Count == 1,
                "other-account, CLI and subagent sources are excluded");

            string bodyOnly = Session("message-only");
            Event(bodyOnly, "agent_message", "message-turn");
            Check(Read().Observations.All(value => value.Snapshot.TaskId != "message-only"),
                "agent_message is not evidence of successful completion");
            Event(main, "item_completed", "unknown-turn", itemType: "AgentMessage");
            Check(Read().Observations.Single().Snapshot.State == AgentTaskState.Unknown,
                "item activity without a matching start does not inherit a previous completion");

            string truncated = Session("truncated");
            File.AppendAllText(truncated, "{\"timestamp\":\"partial", new UTF8Encoding(false));
            Check(Read().Observations.All(value => value.Snapshot.TaskId != "truncated"), "an incomplete final line produces no task state");
            Identity("different-account");
            var switched = Read();
            Check(switched.AccountScopeKey != started.AccountScopeKey && switched.Observations.All(value => value.Snapshot.TaskId == "other-account"),
                "account switch drops cached observations from the previous identity");
            File.Delete(Path.Combine(temporary, ".codex-global-state.json"));
            Check(!Read().SourceAvailable && Read().Observations.Count == 0, "lost identity clears the available source and observations");

            Identity();
            string fast = Session("fast-rounds");
            Event(fast, "task_started", "fast-one"); Event(fast, "task_complete", "fast-one");
            Event(fast, "task_started", "fast-two"); Event(fast, "task_complete", "fast-two");
            Event(fast, "task_started", "fast-three");
            LocalCodexTaskReadResult fastResult = Read();
            Check(fastResult.Observations.Single(value => value.Snapshot.TaskId == "fast-rounds").Snapshot.State == AgentTaskState.Running &&
                fastResult.CompletionEvents.Count(value => value.TaskId == "fast-rounds") == 2,
                "two completed turns between polls survive accounting while latest animation remains third-turn Running");
            clock.Advance(TimeSpan.FromSeconds(1)); Event(fast, "task_complete", "fast-two");
            Check(Read().CompletionEvents.Count(value => value.TaskId == "fast-rounds" && value.TurnId == "fast-two") == 1,
                "same-turn completions with later timestamps merge to one completion evidence item");
            string corrected = Session("corrected-terminal");
            Event(corrected, "task_complete", "corrected");
            Event(corrected, "task_complete", "corrected", error: new { });
            Check(Read().CompletionEvents.All(value => value.TaskId != "corrected-terminal"),
                "unknown error correction in one tail does not leave its earlier normal completion in accounting");
            Event(corrected, "task_complete", "corrected"); Event(corrected, "task_complete", "corrected", error: "explicit failure");
            Check(Read().CompletionEvents.All(value => value.TaskId != "corrected-terminal"),
                "explicit failure correction removes same-turn normal completion evidence");
            Event(corrected, "task_complete", "corrected"); Event(corrected, "turn_aborted", "corrected");
            Check(Read().CompletionEvents.All(value => value.TaskId != "corrected-terminal"),
                "cancel correction removes same-turn normal completion evidence");
            Check(Read().CompletionEvents.All(value => value.TaskId is not ("other-account" or "cli-chat" or "subagent-chat")),
                "accounting evidence uses the same account and desktop-main-chat identity filters as latest state");

            string longTurn = Session("long-turn");
            Event(longTurn, "task_started", "long");
            PushLifecycleOutsideTail(longTurn);
            clock.Advance(TimeSpan.FromMinutes(2));
            Event(longTurn, "item_completed", "long");
            clock.Advance(TimeSpan.FromMinutes(4));
            Event(longTurn, "item_completed", "long");
            clock.Advance(TimeSpan.FromMinutes(2));
            var recovered = Read().Observations.Single(value => value.Snapshot.TaskId == "long-turn");
            Check(recovered.Snapshot.State == AgentTaskState.Running && recovered.TurnId == "long" &&
                  recovered.EventType == "task_started",
                "bounded same-turn lookback finds a start outside 256 KiB and uses the latest matching activity");
            clock.Advance(TimeSpan.FromMinutes(4));
            Check(Read().Observations.Single(value => value.Snapshot.TaskId == "long-turn").Snapshot.State == AgentTaskState.Unknown,
                "a recovered start still expires after the activity freshness window");

            string noMatchingStart = Session("long-without-start");
            Event(noMatchingStart, "task_started", "another-turn");
            PushLifecycleOutsideTail(noMatchingStart);
            Event(noMatchingStart, "item_completed", "no-start");
            Check(Read().Observations.Single(value => value.Snapshot.TaskId == "long-without-start").Snapshot.State == AgentTaskState.Unknown,
                "lookback does not borrow a neighboring turn's start");

            string outsideTerminal = Session("outside-terminal");
            Event(outsideTerminal, "task_complete", "ended");
            PushLifecycleOutsideTail(outsideTerminal);
            Event(outsideTerminal, "item_completed", "ended");
            var recoveredTerminal = Read();
            Check(recoveredTerminal.Observations.Single(value => value.Snapshot.TaskId == "outside-terminal").Snapshot.State == AgentTaskState.Completed &&
                  recoveredTerminal.CompletionEvents.Count(value => value.TaskId == "outside-terminal" && value.TurnId == "ended") == 1,
                "a verified same-turn terminal outside the normal tail resolves the observation and accounting once");

            string lateTerminal = Session("late-same-turn-terminal");
            Event(lateTerminal, "item_completed", "late");
            Event(lateTerminal, "task_complete", "late", eventAt: clock.GetUtcNow().AddSeconds(-1));
            Check(Read().Observations.Single(value => value.Snapshot.TaskId == "late-same-turn-terminal").Snapshot.State == AgentTaskState.Completed,
                "a same-turn completion with an earlier timestamp still resolves unmatched activity");

            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            bool cancellationObserved = false;
            try { source.ReadAsync(cancelled.Token).GetAwaiter().GetResult(); }
            catch (OperationCanceledException) { cancellationObserved = true; }
            Check(cancellationObserved, "read cancellation is honored");
        }
        finally
        {
            // The absolute directory was created by this test, with a fixed task-specific prefix.
            string full = Path.GetFullPath(temporary);
            string parent = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (full.StartsWith(parent + "Lumi-local-task-test-", StringComparison.OrdinalIgnoreCase) && Directory.Exists(full))
                Directory.Delete(full, recursive: true);
        }
    }

    private sealed class TestClock : TimeProvider
    {
        private DateTimeOffset _now = DateTimeOffset.Parse("2026-10-30T01:00:00+08:00");
        public override DateTimeOffset GetUtcNow() => _now.ToUniversalTime();
        public void Advance(TimeSpan value) => _now += value;
    }
}
