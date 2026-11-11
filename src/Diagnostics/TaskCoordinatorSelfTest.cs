// Version: 0.3.14-preview. Generated: 2026-10-30; updated/coverage: 2026-11-11 (Asia/Shanghai).
// Source: LOCAL-WORK / Windows / Codex Desktop / Codex. Original: current user state linkage and startup/account isolation.
using LumiCompanion.Animation;
using LumiCompanion.Behavior;
using LumiCompanion.Integration;
using LumiCompanion.Models;

namespace LumiCompanion.Diagnostics;

public static class TaskCoordinatorSelfTest
{
    public static void Run()
    {
        static void Check(bool ok, string name)
        {
            if (!ok) throw new InvalidOperationException("Task source integration: " + name);
        }
        var clock = new TestClock();
        var behavior = new BehaviorController(new LumiSettings(), clock);
        var coordinator = new LocalTaskStatusCoordinator(behavior, clock);
        LocalCodexTaskReadResult Result(string scope, AgentTaskState state, string version,
            string chat = "fixture-chat", DateTimeOffset? eventAt = null) => new(
                [new(new(LocalCodexTaskSource.SourceId, chat, state, clock.GetUtcNow(), clock.GetUtcNow().AddSeconds(15)),
                    version, eventAt ?? clock.GetUtcNow(), "synthetic")], true, scope, "synthetic fixture", 1, 1);

        coordinator.Apply(Result("a", AgentTaskState.Completed, "old", eventAt: clock.GetUtcNow().AddMinutes(-3)));
        Check(behavior.CurrentAnimation == AnimationId.Idle, "initial historical completion is silent");
        clock.Advance(2);
        coordinator.Apply(Result("a", AgentTaskState.Completed, "old"));
        Check(behavior.CurrentAnimation == AnimationId.Idle, "lease renewal cannot re-alert baseline completion");
        clock.Advance(2);
        coordinator.Apply(Result("a", AgentTaskState.Completed, "new"));
        Check(behavior.CurrentAnimation == AnimationId.Jumping && behavior.CurrentLoopOverride == true,
            "new terminal version starts persistent completion animation");
        coordinator.Apply(new LocalCodexTaskReadResult([], false, "a", "session directory unavailable", 0, 0));
        Check(behavior.CurrentAnimation == AnimationId.Idle, "missing directory disconnects while identity remains known");
        clock.Advance(2);
        coordinator.Apply(Result("a", AgentTaskState.Completed, "new"));
        Check(behavior.CurrentAnimation == AnimationId.Idle, "directory restoration uses a silent terminal baseline");
        clock.Advance(2);
        coordinator.Apply(Result("a", AgentTaskState.Completed, "newer"));
        Check(behavior.CurrentAnimation == AnimationId.Jumping, "new terminal after restored baseline is still announced");
        coordinator.Apply(new LocalCodexTaskReadResult([], false, null, "unavailable fixture", 0, 0));
        Check(behavior.CurrentAnimation == AnimationId.Idle, "losing identity removes prior account state");
        clock.Advance(2);
        coordinator.Apply(Result("b", AgentTaskState.Completed, "other-account"));
        Check(behavior.CurrentAnimation == AnimationId.Idle, "account switch uses another silent baseline");
        clock.Advance(2);
        coordinator.Apply(Result("b", AgentTaskState.Running, "start"));
        Check(behavior.CurrentAnimation == AnimationId.Running, "fresh running state is shown");
        clock.Advance(2);
        coordinator.Apply(Result("c", AgentTaskState.Completed, "account-c"));
        Check(behavior.CurrentAnimation == AnimationId.Idle, "switch clears previous account running state");
        clock.Advance(2);
        coordinator.Apply(Result("c", AgentTaskState.Completed, "historical-new-file", "recovered-old-chat",
            eventAt: clock.GetUtcNow().AddHours(-1)));
        Check(behavior.CurrentAnimation == AnimationId.Idle, "later discovered historical chat does not celebrate");
        clock.Advance(2);
        coordinator.Apply(Result("c", AgentTaskState.Running, "running-lease"));
        clock.Advance(16);
        behavior.Tick();
        Check(behavior.CurrentAnimation == AnimationId.Idle, "missing observations expire instead of remaining running");
        coordinator.Disconnect();
        RunDelayedAcknowledgementScenarios();
        RunPendingTurnScenarios();
        RunWorkBuddyBridgeTiming();
    }

    private static void RunWorkBuddyBridgeTiming()
    {
        var clock = new TestClock();
        var behavior = new BehaviorController(new LumiSettings(), clock);
        var coordinator = new LocalTaskStatusCoordinator(behavior, clock, LocalWorkBuddyTaskSource.SourceId);
        WorkBuddyTaskReadResult Result(string turn, AgentTaskState state, DateTimeOffset updatedAt)
        {
            DateTimeOffset now = clock.GetUtcNow();
            return new(new(LocalWorkBuddyTaskSource.SourceId, "session/" + turn, state, now, now.AddSeconds(15)),
                "workbuddy-fixture-scope", null, []) { UpdatedAt = updatedAt };
        }

        coordinator.Apply(Result("one", AgentTaskState.Running, clock.GetUtcNow()));
        clock.Advance(1);
        DateTimeOffset terminalAt = clock.GetUtcNow();
        clock.Advance(1);
        behavior.HandleDirectInteraction();
        clock.Advance(1);
        coordinator.Apply(Result("one", AgentTaskState.Completed, terminalAt));
        if ((behavior.GetTaskState().WorkBuddyProgress?.CompletedCount ?? 0) != 0)
            throw new InvalidOperationException("WorkBuddy completion delivered after confirmation must honor its original timestamp.");

        clock.Advance(1);
        DateTimeOffset newTerminalAt = clock.GetUtcNow();
        coordinator.Apply(Result("two", AgentTaskState.Completed, newTerminalAt));
        clock.Advance(1);
        coordinator.Apply(Result("two", AgentTaskState.Completed, newTerminalAt));
        if (behavior.GetTaskState().WorkBuddyProgress?.CompletedCount != 1)
            throw new InvalidOperationException("A genuinely new WorkBuddy turn must notify once across repeated bridge reads.");
    }

    private static void RunPendingTurnScenarios()
    {
        static void Check(bool ok, string name)
        {
            if (!ok) throw new InvalidOperationException("Pending completed turns: " + name);
        }
        var clock = new TestClock();
        var behavior = new BehaviorController(new LumiSettings(), clock);
        var coordinator = new LocalTaskStatusCoordinator(behavior, clock);
        ObservedCompletionEvent Completion(string turn, DateTimeOffset at) =>
            new(LocalCodexTaskSource.SourceId, "one-chat", turn, "completion:" + turn, at);
        LocalCodexTaskReadResult Result(AgentTaskState state, string turn, ObservedCompletionEvent[] completions,
            DateTimeOffset? originalEventAt = null, string scope = "scope-a") => new(
                [new(new(LocalCodexTaskSource.SourceId, "one-chat", state, clock.GetUtcNow(), clock.GetUtcNow().AddSeconds(15)),
                    "latest:" + turn + ":" + state, originalEventAt ?? clock.GetUtcNow(), "synthetic") { TurnId = turn }],
                true, scope, "synthetic completed-turn input", 1, 1) { CompletionEvents = completions };

        var historic = Completion("history", clock.GetUtcNow().AddMinutes(-1));
        coordinator.Apply(Result(AgentTaskState.Running, "one", [historic]));
        Check(behavior.GetTaskState().Progress is { ProcessingCount: 1, CompletedCount: 0 },
            "startup completion evidence is quiet while a current chat starts running");
        clock.Advance(1); var one = Completion("one", clock.GetUtcNow());
        clock.Advance(1); var two = Completion("two", clock.GetUtcNow());
        clock.Advance(1);
        coordinator.Apply(Result(AgentTaskState.Running, "three", [historic, one, two]));
        Check(behavior.GetTaskState().Progress is { ProcessingCount: 1, CompletedCount: 2 } &&
              behavior.CurrentAnimation == AnimationId.Running,
            "one poll can add two completed turns while the same chat is already running its third turn");
        clock.Advance(1);
        coordinator.Apply(Result(AgentTaskState.Running, "three", [one with { StateVersion = "duplicate-version" }, two]));
        Check(behavior.GetTaskState().Progress is { ProcessingCount: 1, CompletedCount: 2 },
            "completion evidence and event-version changes still deduplicate by stable turn identity");
        clock.Advance(1); var three = Completion("three", clock.GetUtcNow());
        coordinator.Apply(Result(AgentTaskState.Completed, "three", [one, two, three]));
        Check(behavior.GetTaskState().Progress is { ProcessingCount: 0, CompletedCount: 3, AllCompleted: true },
            "latest Completed and completion evidence for that turn merge into one result");
        clock.Advance(16); behavior.Tick();
        Check(behavior.GetTaskState().Progress is { CompletedCount: 3, AllCompleted: true } &&
              behavior.CurrentAnimation == AnimationId.Jumping,
            "unconfirmed completed turns remain visible beyond all live display leases");
        behavior.HandleDirectInteraction(); behavior.HandleAnimationCompleted(AnimationId.Waving);
        clock.Advance(1);
        coordinator.Apply(Result(AgentTaskState.Completed, "three", [one, two, three], three.EventAt));
        Check(behavior.GetTaskState().Progress is { TotalCount: 0 } && behavior.CurrentAnimation == AnimationId.Idle,
            "confirmation clears the batch and replaying its old turns cannot rebuild it");
        clock.Advance(1); var four = Completion("four", clock.GetUtcNow());
        coordinator.Apply(Result(AgentTaskState.Completed, "four", [four]));
        Check(behavior.GetTaskState().Progress is { CompletedCount: 1 }, "a genuinely new turn may notify after confirmation");
        clock.Advance(1);
        coordinator.Apply(Result(AgentTaskState.Completed, "four", [four], four.EventAt, scope: "scope-b"));
        Check(behavior.GetTaskState().Progress is { TotalCount: 0 }, "account change clears previous-scope turn notifications");

        var delayed = new BehaviorController(new LumiSettings(), clock);
        var delayedCoordinator = new LocalTaskStatusCoordinator(delayed, clock);
        delayedCoordinator.Apply(Result(AgentTaskState.Running, "late-one", []));
        clock.Advance(1); var late = Completion("late-one", clock.GetUtcNow());
        clock.Advance(1); delayed.HandleDirectInteraction();
        clock.Advance(1);
        delayedCoordinator.Apply(Result(AgentTaskState.Running, "late-two", [late]));
        delayed.HandleAnimationCompleted(AnimationId.Waving);
        Check(delayed.GetTaskState().Progress is { ProcessingCount: 1, CompletedCount: 0 } &&
              delayed.CurrentAnimation == AnimationId.Running,
            "a completion before the explicit-interaction watermark cannot return through completion evidence");
    }

    private static void RunDelayedAcknowledgementScenarios()
    {
        static void Check(bool ok, string name)
        {
            if (!ok) throw new InvalidOperationException("Delayed task acknowledgement: " + name);
        }
        foreach (AgentTaskState terminalState in new[] { AgentTaskState.Completed, AgentTaskState.Failed })
        foreach (string interaction in new[] { "click", "double-click", "drag-start", "drag-release", "hover", "late-running" })
        {
            var clock = new TestClock();
            var behavior = new BehaviorController(new LumiSettings(), clock);
            var coordinator = new LocalTaskStatusCoordinator(behavior, clock);
            LocalCodexTaskReadResult Result(AgentTaskState state, string version, DateTimeOffset originalEventAt) => new(
                [new(new(LocalCodexTaskSource.SourceId, "delayed-fixture", state, clock.GetUtcNow(), clock.GetUtcNow().AddSeconds(15)),
                    version, originalEventAt, "synthetic")], true, "synthetic-scope", "synthetic", 1, 1);
            coordinator.Apply(Result(AgentTaskState.Running, "start", clock.GetUtcNow()));
            clock.Advance(1);
            DateTimeOffset terminalAt = clock.GetUtcNow();
            clock.Advance(1);
            if (interaction is "drag-start" or "drag-release") behavior.SetDragging(true);
            else if (interaction == "hover") behavior.SetBodyHovered(true);
            else behavior.HandleDirectInteraction();
            if (interaction == "double-click") behavior.HandleDirectInteraction(energetic: true);
            if (interaction == "drag-release")
            {
                clock.Advance(1); terminalAt = clock.GetUtcNow(); // Turn ends after lift but before release.
                clock.Advance(1); behavior.SetDragging(false);
            }
            clock.Advance(1);
            coordinator.Apply(Result(interaction == "late-running" ? AgentTaskState.Running : terminalState,
                "delayed-state", terminalAt));
            if (interaction == "drag-start") behavior.SetDragging(false);
            if (interaction == "hover") behavior.SetBodyHovered(false);
            behavior.HandleAnimationCompleted(AnimationId.Waving);
            if (interaction == "double-click") behavior.HandleAnimationCompleted(AnimationId.Jumping);
            if (interaction == "hover")
            {
                Check(behavior.CurrentAnimation == (terminalState == AgentTaskState.Failed ? AnimationId.Failed : AnimationId.Jumping) &&
                      behavior.CurrentLoopOverride == (terminalState != AgentTaskState.Failed),
                    "hover does not acknowledge a terminal that arrives later");
                Check(behavior.LastAcknowledgementAt is null, "hover never creates an explicit acknowledgement watermark");
            }
            else if (interaction == "late-running")
                Check(behavior.CurrentAnimation == AnimationId.Running,
                    "acknowledgement watermark never discards still-running work, even with an older event timestamp");
            else
            {
                Check(behavior.CurrentAnimation == AnimationId.Idle,
                    interaction + " suppresses a terminal that happened before acknowledgement but arrived afterward");
                Check(behavior.GetTaskState().Progress is { TotalCount: 0 },
                    interaction + " cannot revive a late acknowledged terminal through current-batch counts");
                Check(behavior.LastAcknowledgementAt.HasValue && terminalAt <= behavior.LastAcknowledgementAt,
                    "only original terminal EventAt is compared to the explicit interaction watermark");
                clock.Advance(1);
                coordinator.Apply(Result(terminalState, "genuinely-new-terminal", clock.GetUtcNow()));
                Check(behavior.CurrentAnimation == (terminalState == AgentTaskState.Failed ? AnimationId.Failed : AnimationId.Jumping) &&
                      behavior.CurrentLoopOverride == (terminalState != AgentTaskState.Failed),
                    "completion that genuinely happens after " + interaction + " still produces a new notification");
            }
        }
    }

    private sealed class TestClock : TimeProvider
    {
        private DateTimeOffset _now = DateTimeOffset.Parse("2026-10-30T00:00:00+08:00");
        public override DateTimeOffset GetUtcNow() => _now.ToUniversalTime();
        public void Advance(int seconds) => _now = _now.AddSeconds(seconds);
    }
}
