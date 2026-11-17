// Version: 0.3.21-preview
// Generated: 2026-10-30 | Updated/Coverage: 2026-11-17 (Asia/Shanghai)
// Source type: RAW (user task, pointer priority and agent separation requirements), LOCAL-WORK (synthetic regressions).
// Node: Windows / Codex Desktop / Codex. Original sources: current Lumi conversation and WorkBuddy bridge handoff V1.0.
using System.Text.Json;
using LumiCompanion.Animation;
using LumiCompanion.Behavior;
using LumiCompanion.Integration;
using LumiCompanion.Models;
using LumiCompanion.Sensors;

namespace LumiCompanion.Diagnostics;

public static class TaskBehaviorSelfTest
{
    public static int Run(string reportPath)
    {
        var checks = new List<string>();
        string? error = null;
        void Check(bool ok, string name)
        {
            if (!ok) throw new InvalidOperationException(name);
            checks.Add(name);
        }
        try
        {
            var clock = new TestClock();
            var behavior = new BehaviorController(new LumiSettings(), clock);
            behavior.SetLookDirection(5);
            int emitted = 0;
            behavior.VisualStateChanged += (_, _) => emitted++;
            AgentTaskSnapshot Snapshot(string id, AgentTaskState state, string source = LocalCodexTaskSource.SourceId, int leaseSeconds = 300) =>
                new(source, id, state, clock.GetUtcNow(), clock.GetUtcNow().AddSeconds(leaseSeconds));
            void Observe(string id, AgentTaskState state, string? version = null, bool announce = true)
            {
                clock.Advance(TimeSpan.FromSeconds(1));
                Check(behavior.ObserveTaskState(Snapshot(id, state), version, announce), $"accept {id} {state} observation");
            }

            Observe("a", AgentTaskState.Completed, "turn-1-completed");
            Check(behavior.CurrentAnimation == AnimationId.Jumping && behavior.CurrentLoopOverride == true,
                "completed state keeps a looping jump notification");
            clock.Advance(TimeSpan.FromSeconds(20));
            behavior.Tick();
            Check(behavior.CurrentAnimation == AnimationId.Jumping && behavior.CurrentLoopOverride == true,
                "terminal notification does not expire as a short reaction");
            int before = emitted;
            Observe("a", AgentTaskState.Completed, "turn-1-completed");
            Check(emitted == before, "same event lease refresh does not restart the visual");
            behavior.SetBodyHovered(true);
            Check(behavior.CurrentAnimation == AnimationId.Look, "body hover temporarily takes over task visual");
            behavior.SetBodyHovered(false);
            Check(behavior.CurrentAnimation == AnimationId.Jumping && behavior.CurrentLoopOverride == true,
                "leaving body restores unacknowledged task notification");
            behavior.HandleDirectInteraction();
            Check(behavior.CurrentAnimation == AnimationId.Waving && behavior.CurrentLoopOverride == false,
                "touch acknowledges terminal state and waves once");
            behavior.HandleAnimationCompleted(AnimationId.Waving);
            Check(behavior.CurrentAnimation == AnimationId.Look, "wave returns to gaze after terminal acknowledgement");
            before = emitted;
            Observe("a", AgentTaskState.Completed, "turn-1-completed");
            Check(behavior.CurrentAnimation == AnimationId.Look && emitted == before,
                "acknowledged event cannot re-alert on repeated reads");
            Observe("a", AgentTaskState.Completed, "turn-2-completed");
            Check(behavior.CurrentAnimation == AnimationId.Jumping && behavior.CurrentLoopOverride == true,
                "new stable version may notify even when state remains completed");
            behavior.HandleDirectInteraction(energetic: true);
            Check(behavior.CurrentAnimation == AnimationId.Waving && behavior.CurrentLoopOverride == false,
                "double touch with pending terminal state acknowledges and waves");
            behavior.HandleAnimationCompleted(AnimationId.Waving);
            behavior.HandleDirectInteraction(energetic: true);
            Check(behavior.CurrentAnimation == AnimationId.Jumping && behavior.CurrentLoopOverride == false,
                "ordinary energetic interaction remains a one-shot jump after acknowledgement");
            behavior.HandleAnimationCompleted(AnimationId.Jumping);

            foreach (AgentTaskState terminalState in new[] { AgentTaskState.Completed, AgentTaskState.Failed })
            {
                var doubleClick = new BehaviorController(new LumiSettings(), clock);
                int doubleClickVisuals = 0;
                doubleClick.VisualStateChanged += (_, _) => doubleClickVisuals++;
                doubleClick.ObserveTaskState(Snapshot("double-click", terminalState), "terminal-before-double-click");
                doubleClick.HandleDirectInteraction(energetic: false); // Actual first button-up.
                int afterFirstUp = doubleClickVisuals;
                clock.Advance(TimeSpan.FromMilliseconds(100));
                doubleClick.HandleDirectInteraction(energetic: true); // Actual second button-down.
                Check(doubleClick.CurrentAnimation == AnimationId.Waving && doubleClick.CurrentLoopOverride == false,
                    $"real double-click callback order preserves {terminalState} acknowledgement wave");
                Check(doubleClickVisuals == afterFirstUp,
                    $"{terminalState} double-click does not restart or replace the first acknowledgement visual");
                Check(doubleClick.LastAcknowledgementAt == clock.GetUtcNow(),
                    $"second {terminalState} double-click callback advances the explicit interaction watermark");
                doubleClick.HandleAnimationCompleted(AnimationId.Waving);
                doubleClick.ObserveTaskState(Snapshot("double-click", terminalState), "terminal-before-double-click");
                Check(doubleClick.CurrentAnimation == AnimationId.Idle,
                    $"{terminalState} lease refresh cannot revive after real double-click confirmation");
                clock.Advance(TimeSpan.FromMilliseconds(1));
                doubleClick.ObserveTaskState(Snapshot("double-click", terminalState), "terminal-after-double-click");
                Check(doubleClick.CurrentAnimation == (terminalState == AgentTaskState.Completed ? AnimationId.Jumping : AnimationId.Failed),
                    $"a new {terminalState} event remains visible after double-click acknowledgement");
            }
            var ordinaryDoubleClick = new BehaviorController(new LumiSettings(), clock);
            ordinaryDoubleClick.HandleDirectInteraction(energetic: false);
            clock.Advance(TimeSpan.FromMilliseconds(100));
            ordinaryDoubleClick.HandleDirectInteraction(energetic: true);
            Check(ordinaryDoubleClick.CurrentAnimation == AnimationId.Jumping && ordinaryDoubleClick.CurrentLoopOverride == false,
                "real double-click callback order still jumps once when there is no terminal to acknowledge");
            ordinaryDoubleClick.HandleAnimationCompleted(AnimationId.Jumping);
            Check(ordinaryDoubleClick.CurrentAnimation == AnimationId.Idle,
                "ordinary double-click jump ends at idle");
            var mixedDoubleClick = new BehaviorController(new LumiSettings(), clock);
            mixedDoubleClick.ObserveTaskState(Snapshot("other-running", AgentTaskState.Running), "still-running");
            mixedDoubleClick.ObserveTaskState(Snapshot("ended", AgentTaskState.Completed), "ended");
            mixedDoubleClick.HandleDirectInteraction(energetic: false);
            clock.Advance(TimeSpan.FromMilliseconds(100));
            mixedDoubleClick.HandleDirectInteraction(energetic: true);
            Check(mixedDoubleClick.CurrentAnimation == AnimationId.Waving,
                "double-click acknowledgement wave survives while another task is running");
            mixedDoubleClick.HandleAnimationCompleted(AnimationId.Waving);
            Check(mixedDoubleClick.CurrentAnimation == AnimationId.Running,
                "real double-click confirmation returns to remaining active work without discarding it");

            Observe("a", AgentTaskState.Completed, "turn-3-completed");
            Observe("b", AgentTaskState.Running, "turn-b-running");
            Check(behavior.CurrentAnimation == AnimationId.Running && behavior.CurrentLoopOverride == true,
                "another running task takes precedence over a single completed task");
            behavior.SetLookDirection(9);
            behavior.Tick();
            Check(behavior.CurrentAnimation == AnimationId.Running,
                "nearby gaze outside the body does not replace task state");
            behavior.HandleInput(new InputActivitySnapshot { MouseEvents = 100, KeyboardEvents = 100 });
            Check(behavior.CurrentAnimation == AnimationId.Running, "passive input reaction cannot steal task visual");
            behavior.HandleDirectInteraction();
            behavior.HandleAnimationCompleted(AnimationId.Waving);
            Check(behavior.CurrentAnimation == AnimationId.Running, "acknowledgement returns to remaining active work");

            Observe("b", AgentTaskState.Idle, "turn-b-idle-before-failure");
            Observe("c", AgentTaskState.Failed, "turn-c-failed");
            Check(behavior.CurrentAnimation == AnimationId.Failed && behavior.CurrentLoopOverride == false &&
                  behavior.CurrentEndFrameOverride == 4, "failed task enters crouch and holds frame four");
            behavior.HandleAnimationCompleted(AnimationId.Failed);
            Check(behavior.CurrentAnimation == AnimationId.Failed, "failed entry completion does not dismiss held crouch");
            behavior.SetBodyHovered(true);
            behavior.SetBodyHovered(false);
            Check(behavior.CurrentAnimation == AnimationId.Failed, "hovering alone does not acknowledge failure");
            Observe("b", AgentTaskState.Running, "turn-b-next-running");
            Check(behavior.CurrentAnimation == AnimationId.Running && behavior.CurrentLoopOverride == true,
                "new Running immediately stands up from an unconfirmed failure");
            behavior.HandleDirectInteraction();
            behavior.HandleAnimationCompleted(AnimationId.Waving);
            Check(behavior.CurrentAnimation == AnimationId.Running, "touching failure returns to another running task");
            Observe("d", AgentTaskState.Waiting, "turn-d-waiting");
            Check(behavior.CurrentAnimation == AnimationId.Waiting && behavior.CurrentLoopOverride == true, "waiting remains a sustained task state");
            Observe("e", AgentTaskState.Review, "turn-e-review");
            Observe("d", AgentTaskState.Idle, "turn-d-idle");
            Observe("b", AgentTaskState.Idle, "turn-b-idle");
            Check(behavior.CurrentAnimation == AnimationId.Review && behavior.CurrentLoopOverride == true, "review survives after higher priority active work ends");
            Observe("e", AgentTaskState.Idle, "turn-e-idle");
            Check(behavior.CurrentAnimation == AnimationId.Look, "acknowledged terminals do not resurrect after active tasks finish");

            Observe("a", AgentTaskState.Completed, "turn-4-completed");
            before = emitted;
            behavior.SetDragging(true);
            Check(behavior.CurrentAnimation == AnimationId.Jumping && behavior.CurrentLoopOverride == false &&
                  behavior.CurrentStartFrameOverride == 2 && behavior.CurrentEndFrameOverride == 2 && emitted > before,
                "drag changes same jump animation to a held airborne frame");
            behavior.HandleDirectInteraction();
            behavior.HandleDragging(rightward: true);
            Check(behavior.CurrentStartFrameOverride == 2, "click during capture and legacy drag notification do not insert a wave or running clip");
            behavior.SetDragging(false);
            Check(behavior.CurrentAnimation == AnimationId.Waving && behavior.CurrentLoopOverride == false,
                "real drag acknowledges the terminal and waves once after release");
            behavior.HandleAnimationCompleted(AnimationId.Waving);
            Check(behavior.CurrentAnimation == AnimationId.Look, "drag acknowledgement returns to gaze without reviving terminal loop");
            behavior.AcknowledgeTaskNotifications();
            Check(behavior.CurrentAnimation == AnimationId.Look, "silent acknowledgement does not synthesize a touch wave");
            behavior.DisconnectTaskSource(LocalCodexTaskSource.SourceId);
            Observe("a", AgentTaskState.Completed, "turn-4-completed");
            Check(behavior.CurrentAnimation == AnimationId.Look, "same acknowledged event stays acknowledged after source reconnect");

            var baseline = new BehaviorController(new LumiSettings(), clock);
            Check(baseline.ObserveTaskState(Snapshot("old", AgentTaskState.Completed), "old-turn", announceTerminal: false), "startup baseline accepts old terminal observation");
            Check(baseline.CurrentAnimation == AnimationId.Idle, "startup baseline does not celebrate historical completion");
            clock.Advance(TimeSpan.FromSeconds(1));
            baseline.ObserveTaskState(Snapshot("old", AgentTaskState.Running), "new-turn");
            Check(baseline.CurrentAnimation == AnimationId.Running, "running state follows immediately during baseline intake");
            clock.Advance(TimeSpan.FromSeconds(1));
            baseline.ObserveTaskState(Snapshot("old", AgentTaskState.Completed), "new-turn-completed");
            Check(baseline.CurrentAnimation == AnimationId.Jumping && baseline.CurrentLoopOverride == true, "new completion after baseline is visible");

            var fallback = new BehaviorController(new LumiSettings(), clock);
            fallback.ObserveTaskState(Snapshot("unversioned", AgentTaskState.Completed));
            fallback.AcknowledgeTaskNotifications();
            clock.Advance(TimeSpan.FromSeconds(1));
            fallback.ObserveTaskState(Snapshot("unversioned", AgentTaskState.Completed));
            Check(fallback.CurrentAnimation == AnimationId.Idle, "unversioned same-state refresh does not create new terminal generation");
            clock.Advance(TimeSpan.FromSeconds(1));
            fallback.ObserveTaskState(Snapshot("unversioned", AgentTaskState.Running));
            clock.Advance(TimeSpan.FromSeconds(1));
            fallback.ObserveTaskState(Snapshot("unversioned", AgentTaskState.Completed));
            Check(fallback.CurrentAnimation == AnimationId.Jumping, "unversioned running-to-completed transition creates a new generation");
            var expiring = new BehaviorController(new LumiSettings(), clock);
            expiring.ObserveTaskState(Snapshot("lease", AgentTaskState.Running, leaseSeconds: 2));
            clock.Advance(TimeSpan.FromSeconds(3));
            expiring.Tick();
            Check(expiring.CurrentAnimation == AnimationId.Idle, "expired task source becomes unknown instead of appearing completed");

            var hovering = new BehaviorController(new LumiSettings(), clock);
            hovering.HandleInput(new InputActivitySnapshot { KeyboardEvents = 10 });
            Check(hovering.CurrentAnimation == AnimationId.Running, "passive input reaction begins without task status");
            hovering.SetBodyHovered(true);
            Check(hovering.CurrentAnimation == AnimationId.Idle, "body hover immediately preempts an earlier passive reaction");
            RunConversationProgressScenarios(Check);
            RunSourceProgressScenario(Check);
            RunPendingTerminalScenarios(Check);
            RunRunningPriorityScenarios(Check);
            RunWorkBuddyScenarios(Check);
        }
        catch (Exception ex) { error = ex.ToString(); }

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(reportPath))!);
        File.WriteAllText(reportPath, JsonSerializer.Serialize(new
        {
            _metadata = new TraceMetadata(),
            scope = "Synthetic Codex, WorkBuddy and additional-agent task states, event versions, pointer inputs and deterministic clock.",
            ok = error is null, checks, error
        }, new JsonSerializerOptions { WriteIndented = true }));
        return error is null ? 0 : 1;
    }

    private static void RunConversationProgressScenarios(Action<bool, string> check)
    {
        var clock = new TestClock();
        void Observe(BehaviorController target, string id, AgentTaskState state, string version,
            string source = LocalCodexTaskSource.SourceId, int leaseSeconds = 300, bool announce = true, string? turn = null)
        {
            clock.Advance(TimeSpan.FromMilliseconds(1));
            DateTimeOffset at = clock.GetUtcNow();
            if (!target.ObserveTaskState(new(source, id, state, at, at.AddSeconds(leaseSeconds)), version, announce,
                turnId: turn ?? version))
                throw new InvalidOperationException("Invalid conversation-count fixture.");
        }
        static ConversationProgress Progress(BehaviorController target) => target.GetTaskState().Progress!;

        var batch = new BehaviorController(new LumiSettings(), clock);
        Observe(batch, "old", AgentTaskState.Completed, "history", announce: false);
        check(Progress(batch).TotalCount == 0, "startup historical terminal records do not enter current batch counts");
        Observe(batch, "same-chat", AgentTaskState.Running, "a-start");
        Observe(batch, "other-chat", AgentTaskState.Running, "b-start");
        check(Progress(batch).TotalCount == 2 && Progress(batch).ProcessingCount == 2,
            "the Codex batch counts each of its parallel chats once");
        Observe(batch, "same-chat", AgentTaskState.Completed, "a-end");
        Observe(batch, "same-chat", AgentTaskState.Completed, "a-end");
        Observe(batch, "same-chat", AgentTaskState.Completed, "a-new-end", turn: "a-end");
        check(Progress(batch).CompletedCount == 1 && Progress(batch).ProcessingCount == 1 && !Progress(batch).AllCompleted,
            "same-turn terminal renewals or another event version do not accumulate completed turns");
        batch.HandleDirectInteraction();
        batch.HandleAnimationCompleted(AnimationId.Waving);
        check(Progress(batch).CompletedCount == 1 && batch.CurrentAnimation == AnimationId.Running,
            "confirming a completed chat retains its batch count while another chat keeps running");
        Observe(batch, "other-chat", AgentTaskState.Review, "b-review");
        check(Progress(batch).ProcessingCount == 1 && Progress(batch).ReviewCount == 1 && batch.GetTaskState().RunningCount == 0,
            "processing count includes review without changing the original running-state counter");
        Observe(batch, "same-chat", AgentTaskState.Running, "a-next-turn");
        check(Progress(batch).CompletedCount == 1 && Progress(batch).ProcessingCount == 2 && Progress(batch).TotalCount == 3,
            "new work in the same chat preserves earlier completed turns while counting each live chat once");
        Observe(batch, "same-chat", AgentTaskState.Completed, "a-next-end");
        Observe(batch, "other-chat", AgentTaskState.Completed, "b-end");
        check(Progress(batch).AllCompleted && Progress(batch).CompletedCount == 3 && batch.CurrentLoopOverride == true,
            "all-completed reports three completed turns across the two chats only after live work ends");
        batch.SetBodyHovered(true); batch.SetBodyHovered(false);
        check(Progress(batch).AllCompleted, "hover alone leaves all-completed counts pending confirmation");
        batch.HandleDirectInteraction();
        check(Progress(batch).TotalCount == 0 && batch.CurrentAnimation == AnimationId.Waving,
            "explicit confirmation clears an ended batch and waves once");
        batch.HandleAnimationCompleted(AnimationId.Waving);
        Observe(batch, "same-chat", AgentTaskState.Completed, "a-end");
        Observe(batch, "same-chat", AgentTaskState.Completed, "a-next-end");
        Observe(batch, "other-chat", AgentTaskState.Completed, "b-end");
        check(Progress(batch).TotalCount == 0 && batch.CurrentAnimation == AnimationId.Idle,
            "repeated acknowledged terminal observations cannot reopen batch counts");

        var failed = new BehaviorController(new LumiSettings(), clock);
        Observe(failed, "done", AgentTaskState.Completed, "done");
        Observe(failed, "error", AgentTaskState.Failed, "error");
        check(Progress(failed).CompletedCount == 1 && Progress(failed).FailedCount == 1 && !Progress(failed).AllCompleted,
            "failed conversation never contributes to successful completion or all-completed claim");
        failed.SetDragging(true); failed.SetDragging(false);
        check(Progress(failed).TotalCount == 0 && failed.CurrentAnimation == AnimationId.Waving,
            "real drag confirmation clears an ended mixed-success/failure batch without claiming success");

        var cancelled = new BehaviorController(new LumiSettings(), clock);
        Observe(cancelled, "cancel", AgentTaskState.Running, "start");
        Observe(cancelled, "done", AgentTaskState.Completed, "done");
        Observe(cancelled, "cancel", AgentTaskState.Idle, "cancelled");
        check(Progress(cancelled).CompletedCount == 1 && Progress(cancelled).InactiveCount == 1 && !Progress(cancelled).AllCompleted,
            "idle or cancelled conversations are not successful completion");
        cancelled.HandleDirectInteraction();
        check(Progress(cancelled).TotalCount == 0, "explicit confirmation can clear mixed completed and cancelled batch");

        var waiting = new BehaviorController(new LumiSettings(), clock);
        Observe(waiting, "done", AgentTaskState.Completed, "done");
        Observe(waiting, "wait", AgentTaskState.Waiting, "wait");
        waiting.HandleDirectInteraction(); waiting.HandleAnimationCompleted(AnimationId.Waving);
        check(Progress(waiting).WaitingCount == 1 && Progress(waiting).ProcessingCount == 0 &&
            Progress(waiting).CompletedCount == 1 && !Progress(waiting).AllCompleted,
            "waiting remains separate from processing and keeps the batch open after confirmation");

        var missing = new BehaviorController(new LumiSettings(), clock);
        Observe(missing, "done", AgentTaskState.Completed, "done");
        Observe(missing, "lost", AgentTaskState.Running, "running", leaseSeconds: 2);
        clock.Advance(TimeSpan.FromSeconds(3)); missing.Tick();
        check(Progress(missing).UnknownCount == 1 && Progress(missing).CompletedCount == 1 && !Progress(missing).AllCompleted,
            "missing active conversation lease becomes unknown and cannot produce all-completed");

        foreach (bool leaseExpires in new[] { false, true })
        {
            var recovering = new BehaviorController(new LumiSettings(), clock);
            Observe(recovering, "already-confirmed", AgentTaskState.Completed, "a-end");
            Observe(recovering, "uncertain", AgentTaskState.Running, "b-start", leaseSeconds: 2);
            recovering.HandleDirectInteraction(); recovering.HandleAnimationCompleted(AnimationId.Waving);
            check(Progress(recovering).CompletedCount == 1 && Progress(recovering).ProcessingCount == 1,
                "confirming A retains its count while B is still active before " + (leaseExpires ? "lease loss" : "unknown status"));
            if (leaseExpires) clock.Advance(TimeSpan.FromSeconds(3));
            else Observe(recovering, "uncertain", AgentTaskState.Unknown, "b-unknown");
            recovering.Tick();
            check(Progress(recovering) is { TotalCount: 2, CompletedCount: 1, UnknownCount: 1, AllCompleted: false },
                "already-confirmed A plus " + (leaseExpires ? "expired B" : "unknown B") + " remains the same unfinished two-chat batch");
            recovering.SetBodyHovered(true); recovering.SetBodyHovered(false);
            recovering.HandleDirectInteraction(); recovering.HandleAnimationCompleted(AnimationId.Waving);
            check(Progress(recovering) is { TotalCount: 2, CompletedCount: 1, UnknownCount: 1, AllCompleted: false },
                "hover or another click cannot erase an unresolved batch member after " + (leaseExpires ? "lease expiry" : "unknown observation"));
            Observe(recovering, "uncertain", AgentTaskState.Running, "b-resumed");
            check(Progress(recovering) is { TotalCount: 2, CompletedCount: 1, ProcessingCount: 1, UnknownCount: 0 },
                "recovering B preserves acknowledged A and the original batch size");
            Observe(recovering, "uncertain", AgentTaskState.Completed, "b-real-end");
            check(Progress(recovering) is { TotalCount: 2, CompletedCount: 2, AllCompleted: true },
                "B genuinely completing reports both chats instead of inventing a new one-chat batch");
            recovering.HandleDirectInteraction(); recovering.HandleAnimationCompleted(AnimationId.Waving);
            check(Progress(recovering).TotalCount == 0, "only the recovered fully ended batch clears on explicit confirmation");
        }

        var retained = new BehaviorController(new LumiSettings(), clock);
        Observe(retained, "early-done", AgentTaskState.Completed, "done", leaseSeconds: 2);
        Observe(retained, "later", AgentTaskState.Running, "running");
        clock.Advance(TimeSpan.FromSeconds(3)); retained.Tick();
        check(Progress(retained).CompletedCount == 1 && Progress(retained).ProcessingCount == 1,
            "explicit completed evidence remains in current batch while other work continues after its display lease");
        Observe(retained, "later", AgentTaskState.Completed, "later-end");
        check(Progress(retained).AllCompleted && Progress(retained).TotalCount == 2,
            "current-batch completed count survives a temporarily absent earlier terminal file");

        var scoped = new BehaviorController(new LumiSettings(), clock);
        Observe(scoped, "a", AgentTaskState.Completed, "done");
        Observe(scoped, "b", AgentTaskState.Running, "running", source: "batch-b");
        scoped.DisconnectTaskSource(LocalCodexTaskSource.SourceId);
        check(Progress(scoped).TotalCount == 0 && scoped.GetTaskState().ProgressBySource?.GetValueOrDefault("batch-b") is
            { TotalCount: 1, CompletedCount: 0, ProcessingCount: 1 },
            "disconnect removes only that source's batch counts and preserves another source's active work");
        scoped.DisconnectTaskSource("batch-b");
        check(scoped.GetTaskState().ProgressBySource is { Count: 0 },
            "source loss clears batch counts instead of carrying them across identity boundaries");
        var disconnectedUnknown = new BehaviorController(new LumiSettings(), clock);
        Observe(disconnectedUnknown, "unknown-member", AgentTaskState.Running, "start");
        Observe(disconnectedUnknown, "unknown-member", AgentTaskState.Unknown, "unknown");
        disconnectedUnknown.DisconnectTaskSource(LocalCodexTaskSource.SourceId);
        check(Progress(disconnectedUnknown).TotalCount == 0, "explicit source disconnect also clears unresolved batch members");
    }

    private static void RunSourceProgressScenario(Action<bool, string> check)
    {
        var clock = new TestClock();
        var behavior = new BehaviorController(new LumiSettings(), clock);
        const string other = "fixture-agent";
        void Observe(string source, AgentTaskState state)
        {
            clock.Advance(TimeSpan.FromMilliseconds(1));
            DateTimeOffset at = clock.GetUtcNow();
            behavior.ObserveTaskState(new(source, "same-chat", state, at, at.AddMinutes(1)), turnId: "same-turn");
        }

        Observe(LocalCodexTaskSource.SourceId, AgentTaskState.Running);
        Observe(LocalWorkBuddyTaskSource.SourceId, AgentTaskState.Waiting);
        Observe(other, AgentTaskState.Completed);
        AggregateTaskState parallel = behavior.GetTaskState();
        check(parallel.ProgressBySource is { Count: 3 } sources &&
            sources[LocalCodexTaskSource.SourceId] is { TotalCount: 1, ProcessingCount: 1, CompletedCount: 0 } &&
            sources[LocalWorkBuddyTaskSource.SourceId] is { TotalCount: 1, WaitingCount: 1 } &&
            sources[other] is { TotalCount: 1, CompletedCount: 1, AllCompleted: true } &&
            parallel.Progress == sources[LocalCodexTaskSource.SourceId] &&
            parallel.WorkBuddyProgress == sources[LocalWorkBuddyTaskSource.SourceId] &&
            parallel.State == AgentTaskState.Waiting,
            "every source owns its batch and animation arbitration still combines all three sources");

        behavior.DisconnectTaskSource(LocalWorkBuddyTaskSource.SourceId);
        AggregateTaskState disconnected = behavior.GetTaskState();
        check(disconnected.ProgressBySource is { Count: 2 } remaining &&
            !remaining.ContainsKey(LocalWorkBuddyTaskSource.SourceId) && remaining.ContainsKey(other) &&
            disconnected.Progress is { ProcessingCount: 1, CompletedCount: 0 } &&
            disconnected.WorkBuddyProgress is { TotalCount: 0 } && disconnected.State == AgentTaskState.Running,
            "disconnecting WorkBuddy leaves Codex and the additional agent in independent batches");

        behavior.HandleDirectInteraction();
        check(behavior.GetTaskState().ProgressBySource is { Count: 1 } active &&
            active[LocalCodexTaskSource.SourceId] is { ProcessingCount: 1 },
            "confirming the additional agent's completed batch retains the other source's active work");
    }

    private static void RunPendingTerminalScenarios(Action<bool, string> check)
    {
        var clock = new TestClock();
        void Observe(BehaviorController target, string chat, string turn, AgentTaskState state,
            string source = LocalCodexTaskSource.SourceId, string? version = null)
        {
            clock.Advance(TimeSpan.FromMilliseconds(1));
            DateTimeOffset at = clock.GetUtcNow();
            if (!target.ObserveTaskState(new(source, chat, state, at, at.AddSeconds(15)),
                version ?? turn + ":" + state, turnId: turn))
                throw new InvalidOperationException("Invalid pending-result fixture.");
        }
        static ConversationProgress Progress(BehaviorController target) => target.GetTaskState().Progress!;
        foreach (AgentTaskState terminal in new[] { AgentTaskState.Completed, AgentTaskState.Failed })
        foreach (bool drag in new[] { false, true })
        {
            var behavior = new BehaviorController(new LumiSettings(), clock);
            Observe(behavior, "A", "a-1", AgentTaskState.Running);
            Observe(behavior, "A", "a-1", terminal);
            clock.Advance(TimeSpan.FromSeconds(16)); behavior.Tick();
            check(behavior.GetTaskState().State == terminal && Progress(behavior).TotalCount == 1,
                $"unconfirmed {terminal} survives its 15-second live lease before another chat starts ({drag})");
            behavior.SetBodyHovered(true); behavior.SetBodyHovered(false);
            check(behavior.GetTaskState().State == terminal && behavior.LastAcknowledgementAt is null,
                $"hover does not confirm an expired {terminal} result ({drag})");
            Observe(behavior, "B", "b-1", AgentTaskState.Running);
            check(Progress(behavior).ProcessingCount == 1 &&
                (terminal == AgentTaskState.Completed ? Progress(behavior).CompletedCount == 1 : Progress(behavior).FailedCount == 1),
                $"new chat combines live work with the earlier expired {terminal} result ({drag})");
            check(behavior.GetTaskState().State == AgentTaskState.Running &&
                behavior.CurrentAnimation == AnimationId.Running && behavior.CurrentLoopOverride == true,
                $"pending {terminal} cannot steal the new chat's Running animation ({drag})");
            Observe(behavior, "A", "a-1", terminal, version: "same-turn-later-event-version");
            check(Progress(behavior).TotalCount == 2, "same-turn refresh after expiry does not add a second terminal result");
            Observe(behavior, "B", "b-1", AgentTaskState.Completed);
            clock.Advance(TimeSpan.FromSeconds(16)); behavior.Tick();
            check(Progress(behavior).CompletedCount == (terminal == AgentTaskState.Completed ? 2 : 1) &&
                Progress(behavior).AllCompleted == (terminal == AgentTaskState.Completed),
                $"two expired terminal results remain confirmable without treating {terminal} as another success ({drag})");
            if (drag) { behavior.SetDragging(true); behavior.SetDragging(false); }
            else behavior.HandleDirectInteraction();
            check(behavior.CurrentAnimation == AnimationId.Waving && Progress(behavior).TotalCount == 0,
                (drag ? "drag" : "click") + " confirms expired result notifications and clears only this batch");
            behavior.HandleAnimationCompleted(AnimationId.Waving);
            Observe(behavior, "A", "a-1", terminal);
            Observe(behavior, "B", "b-1", AgentTaskState.Completed);
            check(behavior.CurrentAnimation == AnimationId.Idle && Progress(behavior).TotalCount == 0,
                "replayed confirmed terminal turns cannot rebuild the cleared batch");
            Observe(behavior, "A", "a-2", AgentTaskState.Completed);
            check(Progress(behavior).CompletedCount == 1, "a genuinely new completed turn can start a new batch");
        }

        var sameChat = new BehaviorController(new LumiSettings(), clock);
        Observe(sameChat, "A", "one", AgentTaskState.Completed);
        clock.Advance(TimeSpan.FromSeconds(16)); sameChat.Tick();
        Observe(sameChat, "A", "two", AgentTaskState.Running);
        check(Progress(sameChat) is { ProcessingCount: 1, CompletedCount: 1 } && sameChat.CurrentAnimation == AnimationId.Running,
            "same chat's next turn keeps the old unconfirmed completion and shows one live chat");
        Observe(sameChat, "A", "two", AgentTaskState.Completed);
        check(Progress(sameChat) is { ProcessingCount: 0, CompletedCount: 2, AllCompleted: true },
            "two turns in one chat count as two completed rounds, not two live chats");

        foreach (AgentTaskState correction in new[] { AgentTaskState.Unknown, AgentTaskState.Idle, AgentTaskState.Failed })
        {
            var revised = new BehaviorController(new LumiSettings(), clock);
            Observe(revised, "A", "older", AgentTaskState.Completed);
            Observe(revised, "A", "revised", AgentTaskState.Completed);
            Observe(revised, "A", "revised", correction);
            check(Progress(revised).CompletedCount == 1 && !Progress(revised).AllCompleted,
                $"same-turn {correction} correction removes only that turn's former success, preserving an older turn");
        }

        var active = new BehaviorController(new LumiSettings(), clock);
        Observe(active, "A", "active", AgentTaskState.Running);
        clock.Advance(TimeSpan.FromSeconds(16)); active.Tick();
        check(active.CurrentAnimation == AnimationId.Idle && Progress(active) is { ProcessingCount: 0, UnknownCount: 1 },
            "latched terminal notifications do not extend a Running lease or invent completion");
        var scoped = new BehaviorController(new LumiSettings(), clock);
        Observe(scoped, "A", "old", AgentTaskState.Completed);
        clock.Advance(TimeSpan.FromSeconds(16)); scoped.Tick();
        Observe(scoped, "B", "live", AgentTaskState.Running, source: "other-source");
        scoped.DisconnectTaskSource(LocalCodexTaskSource.SourceId);
        check(Progress(scoped) is { TotalCount: 0 } && scoped.CurrentAnimation == AnimationId.Running &&
            scoped.GetTaskState().ProgressBySource?.GetValueOrDefault("other-source") is { ProcessingCount: 1, CompletedCount: 0 },
            "explicit source disconnect removes its expired results while another source keeps working");
    }

    private static void RunRunningPriorityScenarios(Action<bool, string> check)
    {
        var clock = new TestClock();
        void Observe(BehaviorController target, string chat, string turn, AgentTaskState state)
        {
            clock.Advance(TimeSpan.FromMilliseconds(1));
            DateTimeOffset at = clock.GetUtcNow();
            if (!target.ObserveTaskState(new(LocalCodexTaskSource.SourceId, chat, state, at, at.AddSeconds(15)),
                turn + ":" + state, turnId: turn))
                throw new InvalidOperationException("Invalid Running-priority fixture.");
        }
        static ConversationProgress Progress(BehaviorController target) => target.GetTaskState().Progress!;

        foreach (bool sameChat in new[] { false, true })
        foreach (bool terminalLeaseExpired in new[] { false, true })
        {
            string scenario = $"sameChat={sameChat}, expiredTerminals={terminalLeaseExpired}";
            var behavior = new BehaviorController(new LumiSettings(), clock);
            behavior.SetLookDirection(5);
            for (int turn = 1; turn <= 11; turn++)
                Observe(behavior, "completed-chat", "completed-" + turn, AgentTaskState.Completed);
            Observe(behavior, "failed-chat", "failed-1", AgentTaskState.Failed);
            Observe(behavior, "failed-chat", "failed-2", AgentTaskState.Failed);
            Observe(behavior, "uncertain-chat", "uncertain-1", AgentTaskState.Running);
            Observe(behavior, "uncertain-chat", "uncertain-1", AgentTaskState.Unknown);
            if (terminalLeaseExpired) clock.Advance(TimeSpan.FromSeconds(16));
            behavior.Tick();
            check(behavior.CurrentAnimation == AnimationId.Failed &&
                Progress(behavior) is { CompletedCount: 11, FailedCount: 2, UnknownCount: 1, ProcessingCount: 0 },
                "Unknown with pending failures and no Running still crouches, preserving 11/2/1 counts: " + scenario);

            string activeChat = sameChat ? "failed-chat" : "new-chat";
            // A different turn ID is essential: a same-turn correction intentionally removes that result.
            Observe(behavior, activeChat, "new-running-turn", AgentTaskState.Running);
            check(behavior.GetTaskState() is { State: AgentTaskState.Running, RunningCount: 1 } &&
                behavior.CurrentAnimation == AnimationId.Running && behavior.CurrentLoopOverride == true &&
                behavior.CurrentEndFrameOverride is null,
                "a fresh Running immediately overrides the older unconfirmed crouch: " + scenario);
            check(Progress(behavior) is { TotalCount: 15, ProcessingCount: 1, CompletedCount: 11, FailedCount: 2, UnknownCount: 1 },
                "Running changes the action while preserving all old completion/failure/Unknown counts: " + scenario);

            behavior.SetBodyHovered(true);
            Observe(behavior, activeChat, "new-running-turn", AgentTaskState.Running);
            check(behavior.CurrentAnimation == AnimationId.Look && behavior.LastAcknowledgementAt is null,
                "body hover keeps P1 priority over refreshed Running without confirming results: " + scenario);
            behavior.SetBodyHovered(false);
            check(behavior.CurrentAnimation == AnimationId.Running,
                "leaving the body restores Running rather than the pending failure: " + scenario);
            behavior.HandleInput(new InputActivitySnapshot { MouseEvents = 100, KeyboardEvents = 100 });
            check(behavior.CurrentAnimation == AnimationId.Running,
                "passive input cannot interrupt Running while old failures remain pending: " + scenario);

            Observe(behavior, activeChat, "new-running-turn", AgentTaskState.Completed);
            check(behavior.GetTaskState().State == AgentTaskState.Failed &&
                behavior.CurrentAnimation == AnimationId.Failed && behavior.CurrentEndFrameOverride == 4 &&
                Progress(behavior) is { ProcessingCount: 0, CompletedCount: 12, FailedCount: 2, UnknownCount: 1 },
                "finishing Running restores unconfirmed failure crouch and preserves mixed counts: " + scenario);

            Observe(behavior, activeChat, "another-running-turn", AgentTaskState.Running);
            Observe(behavior, activeChat, "another-running-turn", AgentTaskState.Unknown);
            check(behavior.CurrentAnimation == AnimationId.Failed &&
                Progress(behavior) is { ProcessingCount: 0, CompletedCount: 12, FailedCount: 2, UnknownCount: 2 },
                "explicit Unknown never invents Running or clears the older failure: " + scenario);
            Observe(behavior, activeChat, "another-running-turn", AgentTaskState.Running);
            clock.Advance(TimeSpan.FromSeconds(16));
            behavior.Tick();
            check(behavior.GetTaskState().RunningCount == 0 && behavior.CurrentAnimation == AnimationId.Failed &&
                Progress(behavior) is { ProcessingCount: 0, CompletedCount: 12, FailedCount: 2, UnknownCount: 2 },
                "expired Running becomes Unknown and restores the unconfirmed failure: " + scenario);
        }

        var dragging = new BehaviorController(new LumiSettings(), clock);
        Observe(dragging, "completed", "old-completion", AgentTaskState.Completed);
        Observe(dragging, "failed", "old-failure", AgentTaskState.Failed);
        Observe(dragging, "working", "live-turn", AgentTaskState.Running);
        dragging.SetDragging(true);
        check(dragging.CurrentAnimation == AnimationId.Jumping && dragging.CurrentStartFrameOverride == 2 &&
            dragging.CurrentEndFrameOverride == 2 && dragging.LastAcknowledgementAt is not null,
            "drag P1 takes over Running and acknowledges the pending terminal results");
        dragging.HandleDragMotion(-100, 0);
        Observe(dragging, "working", "live-turn", AgentTaskState.Running);
        check(dragging.CurrentAnimation == AnimationId.RunningLeft &&
            Progress(dragging) is { ProcessingCount: 1, CompletedCount: 1, FailedCount: 1 },
            "Running refresh cannot steal directional dragging or clear retained terminal counts");
        dragging.SetDragging(false);
        check(dragging.CurrentAnimation == AnimationId.Running &&
            Progress(dragging) is { ProcessingCount: 1, CompletedCount: 1, FailedCount: 1 },
            "drag release returns to live Running while retaining acknowledged terminal counts");
        Observe(dragging, "working", "live-turn", AgentTaskState.Completed);
        check(dragging.CurrentAnimation == AnimationId.Jumping &&
            Progress(dragging) is { CompletedCount: 2, FailedCount: 1, TotalCount: 3 },
            "acknowledged failure stays counted without crouching when the later Running completes");
        dragging.HandleDirectInteraction();
        check(dragging.CurrentAnimation == AnimationId.Waving && Progress(dragging).TotalCount == 0,
            "an ended mixed batch still clears only after explicit confirmation");
    }

    private static void RunWorkBuddyScenarios(Action<bool, string> check)
    {
        var clock = new TestClock();

        LocalTaskStatusCoordinator Bridge(BehaviorController target) =>
            new(target, clock, LocalWorkBuddyTaskSource.SourceId);

        void WorkBuddy(LocalTaskStatusCoordinator target, string turnId, AgentTaskState state,
            int leaseSeconds = 15, DateTimeOffset? updatedAt = null)
        {
            clock.Advance(TimeSpan.FromMilliseconds(1));
            DateTimeOffset now = clock.GetUtcNow();
            string bridgeState = state switch
            {
                AgentTaskState.Waiting => "waiting_user",
                AgentTaskState.Idle => "cancelled",
                AgentTaskState.Unknown => "expired",
                _ => state.ToString().ToLowerInvariant()
            };
            target.Apply(new WorkBuddyTaskReadResult(
                new(LocalWorkBuddyTaskSource.SourceId, turnId, state, now, now.AddSeconds(leaseSeconds)),
                "workspace-fixture", bridgeState, []) { UpdatedAt = updatedAt ?? now });
        }

        void Codex(BehaviorController target, string taskId, AgentTaskState state)
        {
            clock.Advance(TimeSpan.FromMilliseconds(1));
            DateTimeOffset now = clock.GetUtcNow();
            target.ObserveTaskState(new(LocalCodexTaskSource.SourceId, taskId, state, now, now.AddMinutes(5)), turnId: taskId);
        }

        var mixed = new BehaviorController(new LumiSettings(), clock);
        var mixedBridge = Bridge(mixed);
        Codex(mixed, "shared-id", AgentTaskState.Failed);
        WorkBuddy(mixedBridge, "shared-id", AgentTaskState.Running);
        check(mixed.CurrentAnimation == AnimationId.Running &&
            mixed.GetTaskState() is
            {
                Progress: { TotalCount: 1, FailedCount: 1, ProcessingCount: 0 },
                WorkBuddyProgress: { TotalCount: 1, ProcessingCount: 1, FailedCount: 0 }
            },
            "WorkBuddy running beats a Codex failure and leaves the Codex batch counts intact");

        WorkBuddy(mixedBridge, "shared-id", AgentTaskState.Waiting);
        Codex(mixed, "codex-running", AgentTaskState.Running);
        check(mixed.CurrentAnimation == AnimationId.Waiting &&
            mixed.GetTaskState() is
            {
                Progress: { TotalCount: 2, FailedCount: 1, ProcessingCount: 1, WaitingCount: 0 },
                WorkBuddyProgress: { TotalCount: 1, WaitingCount: 1 }
            },
            "WorkBuddy waiting keeps the arbiter's waiting priority without entering the Codex batch");

        WorkBuddy(mixedBridge, "shared-id", AgentTaskState.Failed);
        DateTimeOffset mixedFailedAt = clock.GetUtcNow();
        check(mixed.CurrentAnimation == AnimationId.Running,
            "active Codex work takes priority over a WorkBuddy failure");
        mixed.HandleDirectInteraction();
        mixed.HandleAnimationCompleted(AnimationId.Waving);
        WorkBuddy(mixedBridge, "shared-id", AgentTaskState.Failed, updatedAt: mixedFailedAt);
        check(mixed.CurrentAnimation == AnimationId.Running &&
            mixed.GetTaskState() is
            {
                Progress: { FailedCount: 1, ProcessingCount: 1 },
                WorkBuddyProgress: { TotalCount: 0 }
            },
            "acknowledging WorkBuddy clears its quiet batch while preserving active Codex work and its count");
        mixedBridge.Disconnect();
        check(mixed.GetTaskState() is
            {
                Progress: { FailedCount: 1, ProcessingCount: 1 },
                WorkBuddyProgress: { TotalCount: 0 }
            },
            "disconnecting WorkBuddy leaves the independent Codex batch available");
        Codex(mixed, "codex-running", AgentTaskState.Idle);
        check(mixed.GetTaskState().Progress is { TotalCount: 0 },
            "the acknowledged Codex batch clears when its own active work is gone");

        var behavior = new BehaviorController(new LumiSettings(), clock);
        var bridge = Bridge(behavior);
        behavior.SetLookDirection(5);
        WorkBuddy(bridge, "old", AgentTaskState.Completed);
        DateTimeOffset oldAt = clock.GetUtcNow();
        WorkBuddy(bridge, "old", AgentTaskState.Completed, updatedAt: oldAt);
        check(behavior.CurrentAnimation == AnimationId.Look &&
            behavior.GetTaskState().WorkBuddyProgress is { TotalCount: 0 },
            "initial WorkBuddy completion and its refresh keep the historical turn quiet");

        WorkBuddy(bridge, "fast", AgentTaskState.Completed);
        DateTimeOffset fastAt = clock.GetUtcNow();
        check(behavior.CurrentAnimation == AnimationId.Jumping && behavior.CurrentLoopOverride == true &&
            behavior.GetTaskState() is { Progress: { TotalCount: 0 }, WorkBuddyProgress: { CompletedCount: 1 } },
            "a newly observed WorkBuddy turn can finish between polls and announce without a Codex count");
        behavior.HandleDirectInteraction();
        check(behavior.CurrentAnimation == AnimationId.Waving &&
            behavior.GetTaskState().WorkBuddyProgress is { TotalCount: 0 },
            "click acknowledges a pending WorkBuddy completion");
        behavior.HandleAnimationCompleted(AnimationId.Waving);
        WorkBuddy(bridge, "fast", AgentTaskState.Completed, updatedAt: fastAt);
        WorkBuddy(bridge, "fast", AgentTaskState.Completed, updatedAt: fastAt);
        check(behavior.CurrentAnimation == AnimationId.Look &&
            behavior.GetTaskState().WorkBuddyProgress is { TotalCount: 0 },
            "WorkBuddy lease refresh preserves acknowledgement and restores pointer gaze");

        WorkBuddy(bridge, "failure", AgentTaskState.Running);
        WorkBuddy(bridge, "failure", AgentTaskState.Failed);
        DateTimeOffset failedAt = clock.GetUtcNow();
        check(behavior.CurrentAnimation == AnimationId.Failed && behavior.CurrentEndFrameOverride == 4,
            "a connected WorkBuddy running to failed transition uses the existing failure pose");
        behavior.SetDragging(true);
        behavior.SetDragging(false);
        check(behavior.CurrentAnimation == AnimationId.Waving &&
            behavior.GetTaskState().WorkBuddyProgress is { TotalCount: 0 },
            "real drag acknowledges the WorkBuddy failure on release");
        behavior.HandleAnimationCompleted(AnimationId.Waving);
        WorkBuddy(bridge, "failure", AgentTaskState.Failed, updatedAt: failedAt);
        check(behavior.CurrentAnimation == AnimationId.Look &&
            behavior.GetTaskState().WorkBuddyProgress is { TotalCount: 0 },
            "drag acknowledgement remains effective across WorkBuddy refreshes");

        WorkBuddy(bridge, "expires", AgentTaskState.Running);
        WorkBuddy(bridge, "expires", AgentTaskState.Completed, leaseSeconds: 2);
        check(behavior.CurrentAnimation == AnimationId.Jumping,
            "a connected WorkBuddy running to completed transition announces");
        clock.Advance(TimeSpan.FromSeconds(3));
        behavior.Tick();
        check(behavior.CurrentAnimation == AnimationId.Jumping &&
            behavior.GetTaskState().WorkBuddyProgress is { CompletedCount: 1 },
            "an unacknowledged WorkBuddy completion survives its polling lease just like Codex");

        WorkBuddy(bridge, "next", AgentTaskState.Running);
        check(behavior.CurrentAnimation == AnimationId.Running &&
            behavior.GetTaskState().WorkBuddyProgress is { CompletedCount: 1, ProcessingCount: 1 },
            "a new WorkBuddy turn takes animation priority while retaining the prior result in its batch");
        WorkBuddy(bridge, "next", AgentTaskState.Completed);
        check(behavior.GetTaskState().WorkBuddyProgress is { TotalCount: 2, CompletedCount: 2 },
            "distinct WorkBuddy turns in one workspace share a batch without merging identities");
        behavior.HandleDirectInteraction();
        behavior.HandleAnimationCompleted(AnimationId.Waving);
        WorkBuddy(bridge, "unknown", AgentTaskState.Running);
        WorkBuddy(bridge, "unknown", AgentTaskState.Unknown);
        check(behavior.CurrentAnimation == AnimationId.Look &&
            behavior.GetTaskState().WorkBuddyProgress is { TotalCount: 1, UnknownCount: 1 },
            "an expired WorkBuddy running turn keeps its batch unresolved and releases the task animation");

        WorkBuddy(bridge, "reconnect", AgentTaskState.Running);
        bridge.Disconnect();
        WorkBuddy(bridge, "reconnect", AgentTaskState.Completed);
        check(behavior.CurrentAnimation == AnimationId.Look &&
            behavior.GetTaskState().WorkBuddyProgress is { TotalCount: 0 },
            "first WorkBuddy terminal after reconnect establishes a quiet baseline");

        WorkBuddy(bridge, "delayed", AgentTaskState.Running);
        clock.Advance(TimeSpan.FromSeconds(1));
        DateTimeOffset finishedBeforeClick = clock.GetUtcNow();
        clock.Advance(TimeSpan.FromSeconds(1));
        behavior.HandleDirectInteraction();
        behavior.HandleAnimationCompleted(AnimationId.Waving);
        WorkBuddy(bridge, "delayed", AgentTaskState.Completed, updatedAt: finishedBeforeClick);
        check(behavior.CurrentAnimation == AnimationId.Look &&
            behavior.GetTaskState().WorkBuddyProgress is { TotalCount: 0 },
            "a WorkBuddy terminal first polled after the click uses its bridge event time and stays quiet");
    }

    private sealed class TestClock : TimeProvider
    {
        private DateTimeOffset _now = DateTimeOffset.Parse("2026-10-30T00:00:00+08:00");
        public override DateTimeOffset GetUtcNow() => _now.ToUniversalTime();
        public void Advance(TimeSpan elapsed) => _now += elapsed;
    }
}
