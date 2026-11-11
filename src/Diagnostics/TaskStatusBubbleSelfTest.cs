/*
Version: 0.3.14-preview
Generated: 2026-10-30; updated/coverage: 2026-11-11 (Asia/Shanghai)
Source: RAW (user task bubble requirements), LOCAL-WORK (synthetic WPF and geometry tests).
Node: Windows / Codex Desktop / Codex. Original source: current Lumi conversation.
*/
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using LumiCompanion.Animation;
using LumiCompanion.Behavior;
using LumiCompanion.Infrastructure;
using LumiCompanion.Integration;
using LumiCompanion.Models;
using LumiCompanion.Native;
using LumiCompanion.UI;

namespace LumiCompanion.Diagnostics;

public static class TaskStatusBubbleSelfTest
{
    public static int Run(string reportPath)
    {
        var checks = new List<string>();
        var renders = new List<string>();
        string? error = null;
        string directory = Path.GetDirectoryName(Path.GetFullPath(reportPath))!;
        Directory.CreateDirectory(directory);
        string temporary = Path.Combine(Path.GetTempPath(), "Lumi-bubble-test-" + Guid.NewGuid().ToString("N"));
        string underlayReport = Path.Combine(directory, "bubble-underlay-" + Guid.NewGuid().ToString("N") + ".json");
        Process? underlay = null;
        CompanionWindow? pet = null;
        CodexUsageBadgeWindow? badge = null;
        TaskStatusBubbleWindow? bubble = null;
        void Check(bool ok, string name)
        {
            if (!ok) throw new InvalidOperationException(name);
            checks.Add(name);
        }
        try
        {
            AggregateTaskState State(AgentTaskState state, int running = 0) => new(state, running, state == AgentTaskState.Waiting ? 1 : 0, 1, false);
            ConversationProgress Batch(params AgentTaskState[] states) => ConversationProgress.FromStates(states, currentBatch: true);
            Check(TaskStatusBubbleWindow.Describe(State(AgentTaskState.Running)) == "正在处理 1 条对话", "single running conversation explicitly shows one");
            Check(TaskStatusBubbleWindow.Describe(State(AgentTaskState.Running, 3)) == "正在处理 3 条对话", "multiple running conversations show a count");
            var codex = State(AgentTaskState.Running) with
            {
                Progress = Batch(AgentTaskState.Running)
            };
            var workBuddyComplete = Batch(AgentTaskState.Completed);
            Check(TaskStatusBubbleWindow.Describe(codex with { WorkBuddyProgress = Batch(AgentTaskState.Running) }) ==
                  "Codex · 正在处理 1 条对话\nWorkBuddy · 正在处理 1 条对话",
                  "both sources use the same wording for their independent running counts");
            Check(TaskStatusBubbleWindow.Describe(codex with { WorkBuddyProgress = workBuddyComplete }) ==
                  "Codex · 正在处理 1 条对话\nWorkBuddy · 本批 1 轮已完成\n点击角色确认",
                  "WorkBuddy completion gets the shared confirmation hint without changing the Codex running count");
            var noCodex = codex with { Progress = Batch() };
            Check(TaskStatusBubbleWindow.Describe(noCodex with { WorkBuddyProgress = Batch(AgentTaskState.Unknown) }) ==
                  "WorkBuddy · 1 条状态待核", "an unresolved WorkBuddy batch uses the same wording without creating a Codex entry");
            Check(TaskStatusBubbleWindow.Describe(noCodex with { WorkBuddyProgress = Batch(AgentTaskState.Waiting) }) ==
                  "WorkBuddy · 1 条等你确认" &&
                  TaskStatusBubbleWindow.Describe(noCodex with { WorkBuddyProgress = Batch(AgentTaskState.Failed) }) ==
                  "WorkBuddy · 1 轮遇到问题\n点击角色确认" &&
                  TaskStatusBubbleWindow.Describe(noCodex with { WorkBuddyProgress = Batch(AgentTaskState.Idle) }) is null,
                  "WorkBuddy waiting, failure and idle follow the common batch display rules");
            Check(TaskStatusBubbleWindow.Describe(State(AgentTaskState.Waiting)) == "1 条等你确认" &&
                  TaskStatusBubbleWindow.Describe(State(AgentTaskState.Review)) == "正在处理 1 条对话", "waiting stays separate while review is included in processing");
            Check(TaskStatusBubbleWindow.Describe(State(AgentTaskState.Completed)) == "已完成 1 轮\n点击角色确认" &&
                  TaskStatusBubbleWindow.Describe(State(AgentTaskState.Failed)) == "1 轮遇到问题\n点击角色确认", "terminal wording requests an explicit character interaction");
            Check(TaskStatusBubbleWindow.Describe(State(AgentTaskState.Unknown)) is null &&
                  TaskStatusBubbleWindow.Describe(State(AgentTaskState.Idle)) is null, "unknown and idle hide the bubble");
            var mixedProgress = State(AgentTaskState.Running, 2) with
            { Progress = new ConversationProgress(3, 1, 1, 1, 0, 0, 0, 0, IsCurrentBatch: true) };
            Check(TaskStatusBubbleWindow.Describe(mixedProgress) == "正在处理 2 条对话\n已完成 1 轮",
                "mixed current batch shows processing including review and completed count on separate lines");
            var allCompleted = State(AgentTaskState.Completed) with
            { Progress = new ConversationProgress(3, 0, 0, 3, 0, 0, 0, 0, IsCurrentBatch: true) };
            Check(TaskStatusBubbleWindow.Describe(allCompleted) == "本批 3 轮已完成\n点击角色确认",
                "all-completed wording names only the current observed batch and asks for confirmation");
            Check(TaskStatusBubbleWindow.Describe(allCompleted with { WorkBuddyProgress = workBuddyComplete }) ==
                  "Codex · 本批 3 轮已完成\nWorkBuddy · 本批 1 轮已完成\n点击角色确认",
                  "two ended source batches use the same wording and share one confirmation hint");
            foreach (AgentTaskState blocker in new[] { AgentTaskState.Waiting, AgentTaskState.Failed, AgentTaskState.Unknown, AgentTaskState.Idle })
            {
                var partial = State(AgentTaskState.Completed) with
                { Progress = ConversationProgress.FromStates([AgentTaskState.Completed, blocker], currentBatch: true) };
                Check(TaskStatusBubbleWindow.Describe(partial) is { } message && !message.StartsWith("本批 "),
                    $"{blocker} never permits a batch-wide success message");
                if (blocker == AgentTaskState.Unknown)
                    Check(!TaskStatusBubbleWindow.Describe(partial)!.Contains("确认"),
                        "unknown batch members never invite confirmation as if the whole batch had ended");
            }
            var onlyUnknown = State(AgentTaskState.Unknown) with
            { Progress = ConversationProgress.FromStates([AgentTaskState.Unknown], currentBatch: true) };
            Check(TaskStatusBubbleWindow.Describe(onlyUnknown) == "1 条状态待核",
                "unresolved current batch stays visible without claiming completion or asking to clear it");
            var area = new Rect(-1000, -200, 1000, 800);
            var size = new System.Windows.Size(178, 30);
            foreach (Rect body in new[] { new Rect(-990, -195, 130, 200), new Rect(-140, -195, 130, 200),
                new Rect(-990, 390, 130, 200), new Rect(-140, 390, 130, 200) })
            {
                Rect? planned = TaskStatusBubbleWindow.PlanPlacement(body, area, size, null);
                Check(planned is { } fit && area.Contains(fit) && !fit.IntersectsWith(body), "monitor edge and negative coordinates keep bubble visible without covering body");
            }
            var centerBody = new Rect(250, 200, 100, 180);
            var centerArea = new Rect(0, 0, 800, 600);
            Rect? normal = TaskStatusBubbleWindow.PlanPlacement(centerBody, centerArea, size, null);
            Rect? avoid = TaskStatusBubbleWindow.PlanPlacement(centerBody, centerArea, size, normal);
            Check(normal.HasValue && avoid.HasValue && !avoid.Value.IntersectsWith(normal.Value), "placement avoids an occupied quota card rectangle");
            Check(TaskStatusBubbleWindow.PlanPlacement(new Rect(0, 0, 80, 80), new Rect(0, 0, 80, 80), size, null) is null,
                "no safe room hides the bubble rather than covering the face or leaving the monitor");

            string executable = Environment.ProcessPath ?? throw new InvalidOperationException("Missing executable path.");
            var start = new ProcessStartInfo(executable)
            {
                UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden
            };
            start.ArgumentList.Add("--hit-test-underlay");
            start.ArgumentList.Add(underlayReport);
            underlay = Process.Start(start) ?? throw new InvalidOperationException("Unable to start isolated underlay.");
            var wait = Stopwatch.StartNew();
            while (!File.Exists(underlayReport) && wait.ElapsedMilliseconds < 10000 && !underlay.HasExited) Thread.Sleep(20);
            Check(File.Exists(underlayReport), "isolated cross-process click-through underlay is ready");
            using var underlayData = JsonDocument.Parse(File.ReadAllText(underlayReport));
            IntPtr underlayHandle = new(underlayData.RootElement.GetProperty("hwnd").GetInt64());
            int left = underlayData.RootElement.GetProperty("left").GetInt32();
            int top = underlayData.RootElement.GetProperty("top").GetInt32();
            var catalog = new AnimationCatalog();
            pet = new CompanionWindow(catalog, new LumiSettings { PetWidth = 192, AlwaysOnTop = true });
            pet.ShowWithoutActivation();
            NativeMethods.SetWindowPos(pet.HwndSource!.Handle, IntPtr.Zero, left + 110, top + 160, 0, 0,
                NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);
            pet.SetVisual(AnimationId.Look, 0, "合成气泡测试");
            bubble = new TaskStatusBubbleWindow(pet);
            int visibleTransitions = 0;
            bubble.IsVisibleChanged += (_, _) => visibleTransitions++;
            bubble.SetTaskState(State(AgentTaskState.Running));
            Pump();
            Check(bubble.IsVisible, "real WPF window shows a running bubble");
            IntPtr bubbleHandle = new WindowInteropHelper(bubble).Handle;
            long styles = NativeMethods.GetWindowLongPtr(bubbleHandle, NativeMethods.GWL_EXSTYLE).ToInt64();
            Check(!bubble.IsHitTestVisible && (styles & NativeMethods.WS_EX_TRANSPARENT) != 0 &&
                  (styles & TaskStatusBubbleWindow.NoActivateStyle) != 0, "bubble has independent native click-through and no-activate styles");
            Check(NativeMethods.GetForegroundWindow() != bubbleHandle, "showing bubble does not activate it");
            NativeMethods.GetWindowRect(bubbleHandle, out var standingBubble);
            IntPtr target = NativeMethods.WindowFromPoint(new NativeMethods.POINT
            { X = standingBubble.Left + standingBubble.Width / 2, Y = standingBubble.Top + standingBubble.Height / 2 });
            Check(target == underlayHandle, "point inside bubble resolves to independent underlying window without moving cursor");
            int before = visibleTransitions;
            for (int i = 0; i < 10; i++) bubble.SetTaskState(State(AgentTaskState.Running));
            Pump();
            Check(visibleTransitions == before, "same state refresh does not re-show or blink the bubble");
            SaveComposition(Path.Combine(directory, "bubble-running.png"), pet, bubble);
            renders.Add("bubble-running.png");

            NativeMethods.GetWindowRect(pet.HwndSource.Handle, out var petBefore);
            NativeMethods.SetWindowPos(pet.HwndSource.Handle, IntPtr.Zero, petBefore.Left + 40, petBefore.Top + 15, 0, 0,
                NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);
            Pump();
            NativeMethods.GetWindowRect(bubbleHandle, out var movedBubble);
            Check(Math.Abs(movedBubble.Left - standingBubble.Left - 40) <= 1 &&
                  Math.Abs(movedBubble.Top - standingBubble.Top - 15) <= 1, "bubble follows companion physical window movement");
            Rect standingBody = TaskStatusBubbleWindow.FindBodyBounds(pet.CurrentHitMask!);
            pet.SetVisual(AnimationId.Failed, null, "合成蹲姿", loopOverride: false, startFrameOverride: 4, endFrameOverride: 4);
            bubble.SetTaskState(State(AgentTaskState.Failed));
            Pump();
            Rect crouchingBody = TaskStatusBubbleWindow.FindBodyBounds(pet.CurrentHitMask!);
            NativeMethods.GetWindowRect(bubbleHandle, out var crouchedBubble);
            Check(crouchingBody.Top > standingBody.Top && crouchedBubble.Top > movedBubble.Top + 20,
                "crouched pose lowers bubble anchor to current body instead of original canvas top");
            SaveComposition(Path.Combine(directory, "bubble-crouched.png"), pet, bubble);
            renders.Add("bubble-crouched.png");

            bubble.Dispose();
            badge = new CodexUsageBadgeWindow(pet, new ProviderTabsStore(temporary));
            badge.ApplySettings(new LumiSettings { ShowCodexUsage = true, AlwaysOnTop = true });
            badge.SetExpanded(true);
            bubble = new TaskStatusBubbleWindow(pet, badge);
            bubble.SetTaskState(State(AgentTaskState.Failed));
            Pump();
            Check(bubble.IsVisible && !WindowRect(bubble).IntersectsWith(WindowRect(badge)), "bubble and expanded quota card occupy separate rectangles");
            SaveComposition(Path.Combine(directory, "bubble-with-quota.png"), pet, badge, bubble);
            renders.Add("bubble-with-quota.png");

            var behavior = new BehaviorController(new LumiSettings());
            DateTimeOffset now = DateTimeOffset.UtcNow;
            behavior.ObserveTaskState(new AgentTaskSnapshot("synthetic", "chat", AgentTaskState.Completed, now, now.AddMinutes(5)), "turn-1");
            bubble.SetTaskState(behavior.GetTaskState());
            behavior.SetBodyHovered(true);
            bubble.SetTaskState(behavior.GetTaskState());
            Check(bubble.IsVisible && TaskStatusBubbleWindow.Describe(behavior.GetTaskState()) == "本批 1 轮已完成\n点击角色确认",
                "pointer hover does not erase underlying task status bubble");
            bubble.SetTaskState(allCompleted);
            pet.SetVisual(AnimationId.Jumping, null, "合成本批完成", loopOverride: true);
            Pump();
            SaveComposition(Path.Combine(directory, "bubble-all-completed.png"), pet, bubble);
            renders.Add("bubble-all-completed.png");
            behavior.SetDragging(true);
            bubble.SetTaskState(behavior.GetTaskState());
            Check(!bubble.IsVisible, "explicit drag acknowledgement clears terminal-only bubble");
            behavior.SetDragging(false);
            behavior.HandleDirectInteraction();
            bubble.SetTaskState(behavior.GetTaskState());
            Check(!bubble.IsVisible, "click acknowledgement clears terminal-only bubble through effective aggregate");

            var activeBehavior = new BehaviorController(new LumiSettings());
            DateTimeOffset activeAt = DateTimeOffset.UtcNow;
            activeBehavior.ObserveTaskState(new AgentTaskSnapshot("synthetic", "active-chat", AgentTaskState.Running,
                activeAt, activeAt.AddMinutes(5)), "running-turn");
            activeBehavior.SetDragging(true);
            bubble.SetTaskState(activeBehavior.GetTaskState());
            Check(bubble.IsVisible, "dragging preserves real running bubble while only terminal notifications are acknowledged");

            pet.SetVisual(AnimationId.Running, null, "合成并行处理", loopOverride: true);
            bubble.SetTaskState(mixedProgress); Pump();
            Check(bubble.IsVisible && bubble.Height > 30 &&
                ((System.Windows.Controls.TextBlock)((System.Windows.Controls.Border)bubble.Content).Child).Text ==
                    "正在处理 2 条对话\n已完成 1 轮",
                "actual multiline bubble grows to show both counts without an ellipsis");
            SaveComposition(Path.Combine(directory, "bubble-progress.png"), pet, bubble);
            renders.Add("bubble-progress.png");
            foreach (AgentTaskState sourceState in new[] { AgentTaskState.Running, AgentTaskState.Waiting,
                         AgentTaskState.Completed, AgentTaskState.Failed, AgentTaskState.Unknown })
            {
                bubble.SetTaskState(noCodex with { WorkBuddyProgress = Batch(sourceState) });
                Pump();
                var statusLabel = (System.Windows.Controls.TextBlock)((System.Windows.Controls.Border)bubble.Content).Child;
                statusLabel.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
                Check(bubble.IsVisible && statusLabel.DesiredSize.Width <= bubble.Width - 2,
                    $"WorkBuddy {sourceState} fits without wrapping its source row");
            }
            var reportedProgress = mixedProgress with
            {
                Progress = new ConversationProgress(6, 2, 0, 4, 0, 0, 0, 0, IsCurrentBatch: true),
                WorkBuddyProgress = workBuddyComplete
            };
            bubble.SetTaskState(reportedProgress);
            Pump();
            Check(bubble.IsVisible &&
                ((System.Windows.Controls.TextBlock)((System.Windows.Controls.Border)bubble.Content).Child).Text ==
                    "Codex · 正在处理 2 条对话\n已完成 4 轮\nWorkBuddy · 本批 1 轮已完成\n点击角色确认",
                "mixed bubble preserves both Codex lines and uses the same completion wording for WorkBuddy");
            SaveComposition(Path.Combine(directory, "bubble-codex-workbuddy.png"), pet, bubble);
            renders.Add("bubble-codex-workbuddy.png");

            var sharedBehavior = new BehaviorController(new LumiSettings());
            void Report(string source, string task, string turn, AgentTaskState taskState)
            {
                DateTimeOffset observed = DateTimeOffset.UtcNow;
                sharedBehavior.ObserveTaskState(new AgentTaskSnapshot(source, task, taskState,
                    observed, observed.AddMinutes(5)), turnId: turn);
                bubble.SetTaskState(sharedBehavior.GetTaskState());
                Pump();
            }
            Report("synthetic-codex", "codex-chat", "running", AgentTaskState.Running);
            Report(LocalWorkBuddyTaskSource.SourceId, "workspace-fixture", "done", AgentTaskState.Completed);
            Check(bubble.IsVisible && TaskStatusBubbleWindow.Describe(sharedBehavior.GetTaskState()) ==
                "Codex · 正在处理 1 条对话\nWorkBuddy · 本批 1 轮已完成\n点击角色确认",
                "the real bubble displays WorkBuddy completion alongside still-running Codex work");
            sharedBehavior.HandleDirectInteraction();
            bubble.SetTaskState(sharedBehavior.GetTaskState());
            Check(bubble.IsVisible && TaskStatusBubbleWindow.Describe(sharedBehavior.GetTaskState()) == "正在处理 1 条对话",
                "one click hides WorkBuddy completion immediately without hiding running Codex work");
            Report(LocalWorkBuddyTaskSource.SourceId, "workspace-fixture", "done", AgentTaskState.Completed);
            Check(bubble.IsVisible && TaskStatusBubbleWindow.Describe(sharedBehavior.GetTaskState()) == "正在处理 1 条对话",
                "a repeat poll cannot redraw a confirmed WorkBuddy completion line");
            sharedBehavior.DisconnectTaskSource("synthetic-codex");
            Report(LocalWorkBuddyTaskSource.SourceId, "workspace-fixture", "next", AgentTaskState.Failed);
            Check(bubble.IsVisible && TaskStatusBubbleWindow.Describe(sharedBehavior.GetTaskState()) ==
                "WorkBuddy · 1 轮遇到问题\n点击角色确认",
                "a later WorkBuddy failure remains visible until confirmed");
            sharedBehavior.SetDragging(true);
            bubble.SetTaskState(sharedBehavior.GetTaskState());
            Check(!bubble.IsVisible, "a real drag immediately hides the WorkBuddy-only failure bubble");
            sharedBehavior.SetDragging(false);
            Report(LocalWorkBuddyTaskSource.SourceId, "workspace-fixture", "next", AgentTaskState.Failed);
            Check(!bubble.IsVisible, "a repeat poll cannot reopen a confirmed WorkBuddy-only failure bubble");

            var nextTurn = new BehaviorController(new LumiSettings());
            DateTimeOffset firstTurnAt = DateTimeOffset.UtcNow;
            nextTurn.ObserveTaskCompletion(new ObservedCompletionEvent("synthetic", "same-chat", "first-turn",
                "first-completed", firstTurnAt));
            DateTimeOffset nextTurnAt = DateTimeOffset.UtcNow;
            nextTurn.ObserveTaskState(new AgentTaskSnapshot("synthetic", "same-chat", AgentTaskState.Running,
                nextTurnAt, nextTurnAt.AddMinutes(5)), "next-running", turnId: "next-turn");
            bubble.SetTaskState(nextTurn.GetTaskState()); Pump();
            Check(bubble.IsVisible && bubble.Width == 178 && TaskStatusBubbleWindow.Describe(nextTurn.GetTaskState()) ==
                "正在处理 1 条对话\n已完成 1 轮" && nextTurn.CurrentAnimation == AnimationId.Running,
                "Codex-only next-turn bubble restores its default width and preserves its previous completed round");
            SaveComposition(Path.Combine(directory, "bubble-same-chat-next-turn.png"), pet, bubble);
            renders.Add("bubble-same-chat-next-turn.png");

            var partialUnknown = State(AgentTaskState.Unknown) with
            { Progress = ConversationProgress.FromStates([AgentTaskState.Completed, AgentTaskState.Unknown], currentBatch: true) };
            pet.SetVisual(AnimationId.Idle, null, "合成未决状态");
            bubble.SetTaskState(partialUnknown); Pump();
            Check(bubble.IsVisible && TaskStatusBubbleWindow.Describe(partialUnknown) == "已完成 1 轮\n1 条状态待核",
                "actual unresolved batch keeps completed count without offering to clear unfinished work");
            SaveComposition(Path.Combine(directory, "bubble-partial-unknown.png"), pet, bubble);
            renders.Add("bubble-partial-unknown.png");

            bubble.SetTaskState(State(AgentTaskState.Unknown));
            Check(!bubble.IsVisible, "unknown source hides actual bubble window");
            bubble.SetTaskState(State(AgentTaskState.Idle));
            Check(!bubble.IsVisible, "idle hides actual bubble window");
            bubble.SetTaskState(State(AgentTaskState.Running, 2));
            pet.SetSessionPaused(true);
            Pump();
            Check(!bubble.IsVisible, "hiding or pausing companion hides bubble");
            pet.SetSessionPaused(false);
            Pump();
            Check(bubble.IsVisible, "restored companion shows still-valid status without a new task event");
            IntPtr monitor = NativeMethods.MonitorFromWindow(pet.HwndSource.Handle, NativeMethods.MONITOR_DEFAULTTONEAREST);
            var info = new NativeMethods.MONITORINFO { cbSize = (uint)Marshal.SizeOf<NativeMethods.MONITORINFO>() };
            NativeMethods.GetMonitorInfo(monitor, ref info);
            NativeMethods.SetWindowPos(pet.HwndSource.Handle, IntPtr.Zero, info.rcWork.Left + 3, info.rcWork.Top + 3, 0, 0,
                NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);
            pet.SetVisual(AnimationId.Look, 0, "合成屏幕边界");
            Pump();
            var work = new Rect(info.rcWork.Left, info.rcWork.Top, info.rcWork.Width, info.rcWork.Height);
            Check(bubble.IsVisible && work.Contains(WindowRect(bubble)), "actual top-edge bubble stays inside current monitor work area");
            SaveComposition(Path.Combine(directory, "bubble-monitor-edge.png"), pet, badge, bubble);
            renders.Add("bubble-monitor-edge.png");
        }
        catch (Exception ex) { error = ex.ToString(); }
        finally
        {
            bubble?.Dispose();
            badge?.Dispose();
            if (pet is not null) { pet.RequestExit(); pet.Dispose(); }
            if (underlay is not null)
            {
                if (!underlay.HasExited) { underlay.Kill(); underlay.WaitForExit(3000); }
                underlay.Dispose();
            }
            if (File.Exists(underlayReport)) File.Delete(underlayReport);
            try { if (Directory.Exists(temporary)) Directory.Delete(temporary, true); } catch (IOException) { }
        }
        File.WriteAllText(reportPath, JsonSerializer.Serialize(new
        {
            _metadata = new TraceMetadata(),
            scope = "Synthetic effective task states, mask-based geometry and isolated real WPF window visibility/native click-through. No account access, network, daily config or cursor movement; renders contain only test windows.",
            ok = error is null, checks, renders, error
        }, new JsonSerializerOptions { WriteIndented = true }));
        return error is null ? 0 : 1;
    }

    private static Rect WindowRect(Window window)
    {
        NativeMethods.GetWindowRect(new WindowInteropHelper(window).Handle, out var rect);
        return new Rect(rect.Left, rect.Top, rect.Width, rect.Height);
    }

    private static void SaveComposition(string path, params Window[] windows)
    {
        var visible = windows.Where(window => window.IsVisible).Select(window => (Window: window, Bounds: WindowRect(window))).ToArray();
        Rect union = visible.Select(item => item.Bounds).Aggregate(Rect.Empty, (bounds, item) => { bounds.Union(item); return bounds; });
        union.Inflate(8, 8);
        var visual = new DrawingVisual();
        using (DrawingContext context = visual.RenderOpen())
        {
            context.DrawRectangle(Brushes.DarkSlateBlue, null, new Rect(0, 0, union.Width, union.Height));
            foreach (var item in visible)
            {
                var content = (FrameworkElement)item.Window.Content;
                var bitmap = new RenderTargetBitmap((int)Math.Ceiling(content.ActualWidth * 2), (int)Math.Ceiling(content.ActualHeight * 2), 192, 192, PixelFormats.Pbgra32);
                bitmap.Render(content);
                context.DrawImage(bitmap, new Rect(item.Bounds.Left - union.Left, item.Bounds.Top - union.Top, item.Bounds.Width, item.Bounds.Height));
            }
        }
        var result = new RenderTargetBitmap((int)Math.Ceiling(union.Width * 2), (int)Math.Ceiling(union.Height * 2), 192, 192, PixelFormats.Pbgra32);
        result.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(result));
        using Stream stream = File.Create(path);
        encoder.Save(stream);
    }

    private static void Pump()
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.ApplicationIdle) { Interval = TimeSpan.FromMilliseconds(140) };
        timer.Tick += (_, _) => frame.Continue = false;
        timer.Start();
        try { Dispatcher.PushFrame(frame); } finally { timer.Stop(); }
    }
}
