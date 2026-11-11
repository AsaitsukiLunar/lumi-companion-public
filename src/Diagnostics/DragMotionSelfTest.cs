// Version: 0.3.4-preview; Generated/Updated/Coverage: 2026-10-30 (Asia/Shanghai)
// RAW | user directional-drag regression; LOCAL-WORK | Windows / Codex Desktop / O2 drag-motion subtask / Codex.
// Original sources: existing directional PNGs, captured physical displacement and synthetic lifecycle fixtures.
using System.Text.Json;
using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using LumiCompanion.Animation;
using LumiCompanion.Behavior;
using LumiCompanion.Integration;
using LumiCompanion.Models;
using LumiCompanion.Native;
using LumiCompanion.UI;

namespace LumiCompanion.Diagnostics;

public static class DragMotionSelfTest
{
    public static int Run(string reportPath)
    {
        var checks = new List<string>();
        var emittedSteps = new List<object>();
        var responseTimings = new List<object>();
        string? error = null;
        Application? ownedApplication = null;
        CompanionWindow? pet = null;
        void Check(bool ok, string label)
        {
            if (!ok) throw new InvalidOperationException(label);
            checks.Add(label);
        }
        try
        {
            var clock = new TestClock();
            var behavior = new BehaviorController(new LumiSettings(), clock);
            int changes = 0;
            behavior.VisualStateChanged += (_, _) => changes++;
            bool Held() => behavior.CurrentAnimation == AnimationId.Jumping &&
                behavior.CurrentLoopOverride == false && behavior.CurrentStartFrameOverride == 2 && behavior.CurrentEndFrameOverride == 2;
            behavior.HandleDragMotion(20, 0);
            Check(behavior.CurrentAnimation == AnimationId.Idle, "motion outside drag is ignored");
            behavior.SetDragging(true);
            Check(Held(), "lifting starts at the unchanged airborne frame");
            behavior.HandleDragMotion(8, 0);
            Check(behavior.CurrentAnimation == AnimationId.RunningRight && behavior.CurrentLoopOverride == true &&
                behavior.CurrentStartFrameOverride is null && behavior.CurrentEndFrameOverride is null,
                "rightward displacement loops the original right-running sequence");
            int before = changes;
            clock.Advance(30); behavior.HandleDragMotion(6, 0);
            Check(changes == before, "same direction increments do not restart the clip");
            clock.Advance(10); behavior.HandleDragMotion(-1, 0);
            Check(behavior.CurrentAnimation == AnimationId.RunningRight, "one-pixel reverse jitter does not flip direction");
            clock.Advance(10); behavior.HandleDragMotion(1, 0);
            Check(behavior.CurrentAnimation == AnimationId.RunningRight, "opposing small jitter cancels rather than accumulating direction");
            clock.Advance(10); behavior.HandleDragMotion(-2, 0);
            Check(behavior.CurrentAnimation == AnimationId.RunningRight, "reverse direction has a three-pixel deadzone");
            clock.Advance(10); behavior.HandleDragMotion(-2, 0);
            Check(behavior.CurrentAnimation == AnimationId.RunningLeft && behavior.CurrentLoopOverride == true,
                "real reverse increments switch to the original left-running sequence");
            clock.Advance(179); behavior.Tick();
            Check(behavior.CurrentAnimation == AnimationId.RunningLeft, "short motion pause keeps direction before the still timeout");
            clock.Advance(1); behavior.Tick();
            Check(Held(), "180ms without meaningful horizontal displacement returns to held airborne frame");
            behavior.HandleDragMotion(0, 0);
            Check(Held(), "zero displacement cannot refresh a running direction");
            behavior.HandleDragMotion(0, 15);
            Check(Held(), "vertical lifting stays airborne");
            behavior.HandleDragMotion(1, 12);
            Check(Held(), "vertical lifting with horizontal hand jitter does not run");
            behavior.HandleDragMotion(5, 5);
            Check(behavior.CurrentAnimation == AnimationId.RunningRight, "diagonal movement with meaningful horizontal displacement can run");
            behavior.HandleDragMotion(1, 12);
            Check(Held(), "switching to vertical movement ends horizontal running");
            behavior.HandleDragMotion(1, 0);
            Check(Held(), "cold one-pixel horizontal jitter stays airborne");
            clock.Advance(30); behavior.HandleDragMotion(1, 0);
            Check(behavior.CurrentAnimation == AnimationId.RunningRight, "slow real horizontal increments accumulate past the entry deadzone");
            behavior.SetDragging(false);
            Check(behavior.CurrentAnimation == AnimationId.Idle, "release restores P0 idle immediately");
            behavior.HandleDragMotion(-30, 0);
            Check(behavior.CurrentAnimation == AnimationId.Idle, "late motion after release cannot leave running behind");
            behavior.SetDragging(true);
            Check(Held(), "a new drag does not inherit the previous direction");
            for (int i = 0; i < 6; i++) { clock.Advance(20); behavior.HandleDragMotion(i % 2 == 0 ? 1 : -1, 0); }
            Check(Held(), "alternating one-pixel stationary jitter does not create horizontal running");
            behavior.HandleDragMotion(double.NaN, 0); behavior.HandleDragMotion(double.PositiveInfinity, 1);
            Check(Held(), "invalid displacement values are ignored");
            behavior.SetDragging(false);

            foreach (AgentTaskState state in new[] { AgentTaskState.Running, AgentTaskState.Waiting, AgentTaskState.Review, AgentTaskState.Completed, AgentTaskState.Failed })
            {
                var taskBehavior = new BehaviorController(new LumiSettings(), clock);
                var snapshot = new AgentTaskSnapshot("synthetic-drag", "fixture", state, clock.GetUtcNow(), clock.GetUtcNow().AddMinutes(5));
                taskBehavior.ObserveTaskState(snapshot, "fixture-version");
                AnimationId expected = taskBehavior.CurrentAnimation;
                bool? expectedLoop = taskBehavior.CurrentLoopOverride;
                taskBehavior.SetDragging(true); taskBehavior.HandleDragMotion(-9, 0);
                Check(taskBehavior.CurrentAnimation == AnimationId.RunningLeft,
                    state is AgentTaskState.Completed or AgentTaskState.Failed
                        ? $"P1 drag acknowledges {state} without interrupting directional movement"
                        : $"P1 drag overrides still-active {state} without acknowledging it");
                clock.Advance(1);
                taskBehavior.ObserveTaskState(snapshot with { ObservedAt = clock.GetUtcNow(), ExpiresAt = clock.GetUtcNow().AddMinutes(5) }, "fixture-version");
                Check(taskBehavior.CurrentAnimation == AnimationId.RunningLeft, $"{state} lease refresh cannot steal directional drag");
                taskBehavior.SetDragging(false);
                if (state is AgentTaskState.Completed or AgentTaskState.Failed)
                {
                    Check(taskBehavior.CurrentAnimation == AnimationId.Waving && taskBehavior.CurrentLoopOverride == false,
                        $"real drag acknowledges {state} and gives one release wave");
                    taskBehavior.HandleAnimationCompleted(AnimationId.Waving);
                    Check(taskBehavior.CurrentAnimation == AnimationId.Idle, $"{state} drag acknowledgement returns to P0");
                    clock.Advance(1);
                    taskBehavior.ObserveTaskState(snapshot with { ObservedAt = clock.GetUtcNow(), ExpiresAt = clock.GetUtcNow().AddMinutes(5) }, "fixture-version");
                    Check(taskBehavior.CurrentAnimation == AnimationId.Idle, $"{state} lease renewal cannot revive the acknowledged terminal");
                }
                else
                    Check(taskBehavior.CurrentAnimation == expected && taskBehavior.CurrentLoopOverride == expectedLoop,
                        $"release restores still-active {state} without a terminal wave");
            }
            var hovered = new BehaviorController(new LumiSettings(), clock);
            hovered.SetLookDirection(3); hovered.SetBodyHovered(true); hovered.SetDragging(true); hovered.HandleDragMotion(9, 0);
            hovered.SetDragging(false);
            Check(hovered.CurrentAnimation == AnimationId.Look, "release restores current body-hover gaze when appropriate");

            var hoverOnly = new BehaviorController(new LumiSettings(), clock);
            var terminal = new AgentTaskSnapshot("synthetic-terminal", "fixture", AgentTaskState.Completed, clock.GetUtcNow(), clock.GetUtcNow().AddMinutes(5));
            hoverOnly.ObserveTaskState(terminal, "terminal");
            hoverOnly.SetBodyHovered(true); hoverOnly.SetBodyHovered(false);
            Check(hoverOnly.CurrentAnimation == AnimationId.Jumping && hoverOnly.CurrentLoopOverride == true && hoverOnly.LastAcknowledgementAt is null,
                "hover alone neither acknowledges terminal state nor advances the explicit-interaction watermark");
            var burst = new BehaviorController(new LumiSettings(), clock);
            burst.HandleDirectInteraction(); burst.HandleAnimationCompleted(AnimationId.Waving);
            clock.Advance(100);
            burst.ObserveTaskState(terminal with { ObservedAt = clock.GetUtcNow() }, "terminal-one");
            burst.HandleDirectInteraction();
            Check(burst.CurrentAnimation == AnimationId.Waving && burst.CurrentLoopOverride == false,
                "new terminal click acknowledgement bypasses the direct-wave cooldown");
            burst.HandleAnimationCompleted(AnimationId.Waving);
            clock.Advance(100);
            burst.ObserveTaskState(terminal with { ObservedAt = clock.GetUtcNow() }, "terminal-two");
            burst.SetDragging(true); burst.HandleDragMotion(9, 0);
            Check(burst.CurrentAnimation == AnimationId.RunningRight, "acknowledged terminal does not insert a wave while dragging");
            clock.Advance(10); burst.SetDragging(false);
            Check(burst.CurrentAnimation == AnimationId.Waving, "terminal drag-release wave also bypasses a recent direct-wave cooldown");
            burst.HandleAnimationCompleted(AnimationId.Waving);
            Check(burst.CurrentAnimation == AnimationId.Idle, "burst acknowledgement leaves no pending terminal loop");
            var duringDrag = new BehaviorController(new LumiSettings(), clock);
            duringDrag.SetDragging(true); duringDrag.HandleDragMotion(-8, 0);
            clock.Advance(10);
            duringDrag.ObserveTaskState(terminal with { ObservedAt = clock.GetUtcNow() }, "ended-during-drag");
            Check(duringDrag.CurrentAnimation == AnimationId.RunningLeft, "new terminal arriving while dragging does not steal P1");
            clock.Advance(10); duringDrag.SetDragging(false);
            Check(duringDrag.CurrentAnimation == AnimationId.Waving, "release acknowledges a terminal observed during the same drag");
            duringDrag.HandleAnimationCompleted(AnimationId.Waving);
            Check(duringDrag.CurrentAnimation == AnimationId.Idle, "terminal observed during drag does not revive after release");

            if (Application.Current is null) ownedApplication = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            if (System.Windows.Input.Mouse.LeftButton != System.Windows.Input.MouseButtonState.Released)
                throw new InvalidOperationException("Mouse is currently pressed; isolated capture test must run after release to avoid interrupting user input.");
            var settings = new LumiSettings
            {
                PetWidth = 192, AlwaysOnTop = true, MouseLook = false,
                MonitorInputActivity = false, MonitorWindows = false, MonitorAudio = false,
                PositionX = SystemParameters.WorkArea.Left + 80, PositionY = SystemParameters.WorkArea.Top + 80
            };
            pet = new CompanionWindow(new AnimationCatalog(), settings);
            var wired = new BehaviorController(settings, clock);
            int starts = 0, ends = 0, interactions = 0;
            wired.VisualStateChanged += (_, e) => pet.SetVisual(e.Animation, e.LookIndex, e.Hint, e.LoopOverride, e.StartFrameOverride, e.EndFrameOverride);
            pet.DraggingChanged += active => { if (active) starts++; else ends++; wired.SetDragging(active); };
            pet.DragMotionChanged += (dx, dy) => { emittedSteps.Add(new { dx, dy }); wired.HandleDragMotion(dx, dy); };
            pet.InteractionRequested += (_, energetic) => { interactions++; wired.HandleDirectInteraction(energetic); };
            pet.ShowWithoutActivation(); Pump();
            var task = new AgentTaskSnapshot("synthetic-window", "fixture", AgentTaskState.Running, clock.GetUtcNow(), clock.GetUtcNow().AddMinutes(5));
            wired.ObserveTaskState(task, "running");
            Check(pet.BeginBodyDrag(new NativeMethods.POINT { X = 100, Y = 100 }), "real input layer captures a synthetic body press");
            pet.MoveCapturedBody(new NativeMethods.POINT { X = 101, Y = 100 });
            Check(starts == 0 && emittedSteps.Count == 0, "sub-threshold movement emits neither drag-start nor motion");
            var response = Stopwatch.StartNew();
            pet.MoveCapturedBody(new NativeMethods.POINT { X = 120, Y = 100 });
            responseTimings.Add(new { operation = "cold capture threshold plus held-frame and right-running mask", elapsedMilliseconds = response.Elapsed.TotalMilliseconds });
            Pump();
            Check(starts == 1 && wired.CurrentAnimation == AnimationId.RunningRight, "captured right movement reaches P1 behavior through real events");
            pet.MoveCapturedBody(new NativeMethods.POINT { X = 125, Y = 100 });
            response.Restart(); pet.MoveCapturedBody(new NativeMethods.POINT { X = 115, Y = 100 });
            responseTimings.Add(new { operation = "first left-running transition and mask", elapsedMilliseconds = response.Elapsed.TotalMilliseconds });
            Pump();
            Check(wired.CurrentAnimation == AnimationId.RunningLeft, "captured reverse movement changes actual display direction");
            Check(emittedSteps.Count == 3 && JsonSerializer.Serialize(emittedSteps).Contains("\"dx\":-10", StringComparison.Ordinal),
                "motion events report physical increments, not cumulative drag offset");
            pet.MoveCapturedBody(new NativeMethods.POINT { X = 115, Y = 120 }); Pump();
            Check(wired.CurrentAnimation == AnimationId.Jumping && wired.CurrentStartFrameOverride == 2,
                "captured vertical movement returns to the held airborne display");
            pet.MoveCapturedBody(new NativeMethods.POINT { X = 100, Y = 100 });
            pet.EndBodyDrag(notify: true); Pump();
            Check(ends == 1 && interactions == 0 && wired.CurrentAnimation == AnimationId.Running,
                "out-and-back drag remains a drag and restores task state without generating a click");
            Check(pet.BeginBodyDrag(new NativeMethods.POINT { X = 100, Y = 100 }), "subsequent body click captures normally");
            pet.MoveCapturedBody(new NativeMethods.POINT { X = 101, Y = 100 });
            pet.EndBodyDrag(notify: true);
            Check(starts == 1 && interactions == 1 && wired.CurrentAnimation == AnimationId.Waving,
                "press-release with sub-threshold jitter stays a single click rather than directional drag");
            wired.HandleAnimationCompleted(AnimationId.Waving);
            Check(wired.CurrentAnimation == AnimationId.Running, "single-click response returns to P3 running work");
            clock.Advance(1);
            var ended = task with { State = AgentTaskState.Completed, ObservedAt = clock.GetUtcNow() };
            wired.ObserveTaskState(ended, "window-terminal");
            Check(pet.BeginBodyDrag(new NativeMethods.POINT { X = 100, Y = 100 }), "pending terminal can start a real captured drag");
            pet.MoveCapturedBody(new NativeMethods.POINT { X = 120, Y = 100 });
            pet.MoveCapturedBody(new NativeMethods.POINT { X = 100, Y = 100 });
            response.Restart(); pet.EndBodyDrag(notify: true);
            responseTimings.Add(new { operation = "terminal drag release to acknowledgement wave", elapsedMilliseconds = response.Elapsed.TotalMilliseconds });
            Check(interactions == 1 && wired.CurrentAnimation == AnimationId.Waving,
                "real terminal drag acknowledges without fabricating a click even after returning to its origin");
            wired.HandleAnimationCompleted(AnimationId.Waving);
            clock.Advance(1);
            wired.ObserveTaskState(ended with { ObservedAt = clock.GetUtcNow() }, "window-terminal");
            Check(wired.CurrentAnimation == AnimationId.Idle, "actual captured terminal acknowledgement does not revive on source renewal");
        }
        catch (Exception ex) { error = ex.ToString(); }
        finally
        {
            if (pet is not null) { pet.EndBodyDrag(notify: false); pet.RequestExit(); pet.Dispose(); }
            ownedApplication?.Shutdown();
        }
        string path = Path.GetFullPath(reportPath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(new
        {
            _metadata = new TraceMetadata(),
            scope = "Synthetic monotonic event order and controlled clock plus isolated WPF capture/physical motion events. Cursor never moved; daily pet/config/source untouched.",
            stillDelayMilliseconds = DragMotionTracker.StillDelay.TotalMilliseconds,
            ok = error is null, checks, emittedSteps, responseTimings, error
        }, new JsonSerializerOptions { WriteIndented = true }));
        return error is null ? 0 : 1;
    }

    private static void Pump()
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.ApplicationIdle) { Interval = TimeSpan.FromMilliseconds(40) };
        timer.Tick += (_, _) => frame.Continue = false;
        timer.Start(); try { Dispatcher.PushFrame(frame); } finally { timer.Stop(); }
    }

    private sealed class TestClock : TimeProvider
    {
        private DateTimeOffset _now = DateTimeOffset.Parse("2026-10-30T02:00:00+08:00");
        public override DateTimeOffset GetUtcNow() => _now.ToUniversalTime();
        public void Advance(int milliseconds) => _now = _now.AddMilliseconds(milliseconds);
    }
}
