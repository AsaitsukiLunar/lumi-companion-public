// Version: 0.3.1-preview
// Generated/Updated/Coverage: 2026-10-30 (Asia/Shanghai)
// Source type: LOCAL-WORK; Node: Windows / Codex Desktop / Codex
// Original source: current user repair request and local animation/behavior regressions.
using System.Diagnostics;
using System.Text.Json;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using LumiCompanion.Animation;
using LumiCompanion.Behavior;
using LumiCompanion.Models;
using LumiCompanion.Sensors;

namespace LumiCompanion.Diagnostics;

public static class AnimationSelfTest
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
            var single = new AnimationPlayback(4, TimeSpan.FromMilliseconds(100), false);
            Check(!single.Advance(TimeSpan.FromMilliseconds(350)) && single.FrameIndex == 3,
                "delayed tick reaches final frame without stretching clip");
            Check(single.Advance(TimeSpan.FromMilliseconds(50)) && single.Completed && single.FrameIndex == 3,
                "one-shot completes only after final frame duration");
            Check(!single.Advance(TimeSpan.FromSeconds(10)) && single.FrameIndex == 3,
                "completion fires once and holds last frame");
            var loop = new AnimationPlayback(4, TimeSpan.FromMilliseconds(100), true);
            Check(!loop.Advance(TimeSpan.FromMilliseconds(950)) && loop.FrameIndex == 1 && !loop.Completed,
                "loop wraps by elapsed time after delayed ticks");

            var settings = new LumiSettings();
            var clock = new TestClock();
            var behavior = new BehaviorController(settings, clock);
            behavior.SetLookDirection(4);
            behavior.Tick();
            behavior.HandleInput(new InputActivitySnapshot { MouseEvents = 100, IdleSeconds = 0 });
            behavior.Tick();
            Check(behavior.CurrentAnimation == AnimationId.Look, "mouse activity preserves active gaze");
            behavior.SetLookDirection(null);
            behavior.HandleInput(new InputActivitySnapshot { MouseEvents = 100 });
            Check(behavior.CurrentAnimation == AnimationId.Review, "mouse reaction retained outside gaze radius");
            behavior.SetLookDirection(7);
            behavior.Tick();
            Check(behavior.CurrentAnimation == AnimationId.Look, "entering gaze cancels previous mouse reaction");
            behavior.HandleDirectInteraction(energetic: true);
            behavior.HandleInput(new InputActivitySnapshot { MouseEvents = 100 });
            behavior.Tick();
            Check(behavior.CurrentAnimation == AnimationId.Jumping, "direct interaction keeps priority over gaze");
            behavior.HandleAnimationCompleted(AnimationId.Waving);
            Check(behavior.CurrentAnimation == AnimationId.Jumping, "stale completion does not cancel another action");
            behavior.HandleAnimationCompleted(AnimationId.Jumping);
            Check(behavior.CurrentAnimation == AnimationId.Look, "completed action returns to gaze");
            behavior.SetLookDirection(null);
            behavior.HandleDirectInteraction();
            behavior.HandleAnimationCompleted(AnimationId.Waving);
            Check(behavior.CurrentAnimation == AnimationId.Idle, "completed action returns to idle without gaze");
            clock.Advance(TimeSpan.FromSeconds(1));
            behavior.HandleDirectInteraction(energetic: true);
            Check(behavior.CurrentAnimation == AnimationId.Jumping, "timeout recovery starts with an active action");
            clock.Advance(TimeSpan.FromSeconds(3));
            behavior.Tick();
            Check(behavior.CurrentAnimation == AnimationId.Idle, "reaction timeout remains a recovery fallback");
            settings.MouseLook = false;
            clock.Advance(TimeSpan.FromSeconds(10));
            behavior.SetLookDirection(2);
            behavior.HandleInput(new InputActivitySnapshot { MouseEvents = 100 });
            Check(behavior.CurrentAnimation == AnimationId.Review, "disabled gaze preserves mouse reaction setting");

            var catalog = new AnimationCatalog();
            Check(!catalog.Get(AnimationId.Waving).Loop && !catalog.Get(AnimationId.Jumping).Loop &&
                  !catalog.Get(AnimationId.Failed).Loop && catalog.Get(AnimationId.Running).Loop,
                "catalog distinguishes one-shot and looping actions");
            using var presenter = new SpritePresenter(catalog);
            int completed = 0;
            presenter.AnimationCompleted += _ => completed++;
            presenter.SetAnimation(AnimationId.Waving, null, false);
            PumpUntil(() => completed == 1, 2000);
            Check(VisibleSource(presenter) == catalog.Get(AnimationId.Waving).Frames[^1],
                "real WPF timer completes at the final frame");
            PumpFor(180);
            Check(completed == 1, "WPF does not replay a completed one-shot");

            presenter.SetAnimation(AnimationId.Jumping, null, false);
            PumpUntil(() => VisibleSource(presenter) == catalog.Get(AnimationId.Jumping).Frames[1], 1000);
            presenter.SetPaused(true);
            BitmapSource? pausedFrame = VisibleSource(presenter);
            PumpFor(650);
            Check(completed == 1 && VisibleSource(presenter) == pausedFrame, "pause freezes playback without completing");
            presenter.SetPaused(false);
            PumpUntil(() => completed == 2, 2000);
            Check(completed == 2, "resume finishes remaining playback");

            presenter.SetAnimation(AnimationId.Look, 4);
            Check(SingleVisibleFrame(presenter) && VisibleSource(presenter) == catalog.GetLookFrame(4),
                "entering gaze shows one character without crossfade");
            presenter.SetAnimation(AnimationId.Look, 5);
            Check(SingleVisibleFrame(presenter) && VisibleSource(presenter) == catalog.GetLookFrame(5),
                "direction changes show one character without crossfade");
            presenter.SetAnimation(AnimationId.Idle, null);
            Check(SingleVisibleFrame(presenter), "leaving gaze has no whole-character overlap");

            // Use the real completion callback to return to gaze and ensure reentrant changes are safe.
            var liveBehavior = new BehaviorController(new LumiSettings());
            liveBehavior.SetLookDirection(3);
            liveBehavior.VisualStateChanged += (_, e) => presenter.SetAnimation(e.Animation, e.LookIndex);
            presenter.AnimationCompleted += liveBehavior.HandleAnimationCompleted;
            liveBehavior.HandleDirectInteraction();
            PumpUntil(() => liveBehavior.CurrentAnimation == AnimationId.Look, 2000);
            Check(VisibleSource(presenter) == catalog.GetLookFrame(3), "WPF completion callback restores latest gaze");

            presenter.AnimationCompleted -= liveBehavior.HandleAnimationCompleted;
            int beforeLoop = completed;
            presenter.SetAnimation(AnimationId.Jumping, null, false, loopOverride: true);
            PumpFor(1200);
            Check(completed == beforeLoop, "task completion jump loops without firing one-shot completion");
            presenter.SetAnimation(AnimationId.Failed, null, false, loopOverride: false, endFrameOverride: 4);
            PumpUntil(() => completed == beforeLoop + 1, 2000);
            Check(VisibleSource(presenter) == catalog.Get(AnimationId.Failed).Frames[4],
                "failed task holds crouch frame instead of returning to standing frame seven");
            presenter.SetAnimation(AnimationId.Jumping, null, false, loopOverride: false,
                startFrameOverride: 2, endFrameOverride: 2);
            PumpFor(250);
            Check(VisibleSource(presenter) == catalog.Get(AnimationId.Jumping).Frames[2],
                "pickup clip holds existing airborne pose");
            presenter.SetAnimation(AnimationId.Jumping, null, false, loopOverride: true);
            PumpUntil(() => VisibleSource(presenter) != catalog.Get(AnimationId.Jumping).Frames[2], 1000);
            Check(completed == beforeLoop + 2, "changing playback mode on the same action restarts its new clip");
        }
        catch (Exception ex)
        {
            error = ex.ToString();
        }

        var report = new
        {
            _metadata = new TraceMetadata(),
            scope = "Synthetic behavior inputs and real WPF Dispatcher playback; no live sensors or installed pet changes.",
            ok = error is null,
            checks,
            error
        };
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(reportPath))!);
        File.WriteAllText(reportPath, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        return error is null ? 0 : 1;
    }

    private static BitmapSource? VisibleSource(SpritePresenter presenter) => presenter.Children.OfType<Image>()
        .Where(image => image.Opacity > 0.99).Select(image => image.Source as BitmapSource).FirstOrDefault();

    private static bool SingleVisibleFrame(SpritePresenter presenter) => presenter.Children.OfType<Image>()
        .Count(image => image.Opacity > 0 && image.Source is not null) == 1;

    private static void PumpFor(int milliseconds) => PumpUntil(() => false, milliseconds, allowTimeout: true);

    private static void PumpUntil(Func<bool> finished, int timeoutMilliseconds, bool allowTimeout = false)
    {
        var frame = new DispatcherFrame();
        var elapsed = Stopwatch.StartNew();
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(10) };
        timer.Tick += (_, _) =>
        {
            if (finished() || elapsed.ElapsedMilliseconds >= timeoutMilliseconds) frame.Continue = false;
        };
        timer.Start();
        try { Dispatcher.PushFrame(frame); }
        finally { timer.Stop(); }
        if (!allowTimeout && !finished()) throw new TimeoutException("WPF playback did not reach the expected state.");
    }

    private sealed class TestClock : TimeProvider
    {
        private DateTimeOffset _now = DateTimeOffset.Parse("2026-10-30T00:00:00+08:00");
        public override DateTimeOffset GetUtcNow() => _now.ToUniversalTime();
        public void Advance(TimeSpan elapsed) => _now += elapsed;
    }
}
