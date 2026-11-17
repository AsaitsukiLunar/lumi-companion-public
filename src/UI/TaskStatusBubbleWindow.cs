/*
Version: 0.3.21-preview
Generated: 2026-10-30; updated / coverage: 2026-11-17 (Asia/Shanghai)
Source type: RAW (user task bubble and separate Agent source requirements); LOCAL-WORK (implementation); LOCAL-AGENT (WorkBuddy state bridge handoff V1.0).
Source: Lumi Companion / Codex
Organizing node: Windows / Codex Desktop / Codex
Original sources: current user requirement; existing account-scoped effective task state and sprite body mask.
*/
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using LumiCompanion.Integration;
using LumiCompanion.Models;
using LumiCompanion.Native;
using Controls = System.Windows.Controls;
using MediaColor = System.Windows.Media.Color;

namespace LumiCompanion.UI;

/// <summary>Non-interactive high-level status only. No task text, reasoning, network or account access.</summary>
public sealed class TaskStatusBubbleWindow : Window, IDisposable
{
    internal const long NoActivateStyle = 0x08000000L;
    private const double DefaultWidth = 178;
    private const double MaxBubbleWidth = 216;
    private readonly CompanionWindow _companion;
    private readonly Controls.TextBlock _label;
    private readonly DispatcherTimer _placementTimer;
    private readonly ScaleTransform _scale = new();
    private string? _message;
    private double _elevation;
    private bool _disposed;
    private bool _allowClose;
    private IntPtr _handle;

    public TaskStatusBubbleWindow(CompanionWindow companion)
    {
        _companion = companion;
        Owner = companion;
        Title = "Lumi · 当前状态";
        Width = DefaultWidth;
        Height = 30;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        IsHitTestVisible = false;
        Topmost = companion.Topmost;
        UseLayoutRounding = true;
        SnapsToDevicePixels = true;
        _label = new Controls.TextBlock
        {
            Text = string.Empty, Foreground = Brushes.White, FontSize = 11,
            TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(9, 4, 9, 4)
        };
        Content = new Controls.Border
        {
            Background = new SolidColorBrush(MediaColor.FromArgb(235, 25, 29, 43)),
            BorderBrush = new SolidColorBrush(MediaColor.FromArgb(220, 108, 224, 255)),
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Child = _label,
            LayoutTransform = _scale
        };
        _placementTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(100) };
        _placementTimer.Tick += OnPlacementTimer;
        SourceInitialized += OnSourceInitialized;
        Closing += OnClosing;
        _companion.LocationChanged += OnCompanionPlacementChanged;
        _companion.SizeChanged += OnCompanionPlacementChanged;
        _companion.IsVisibleChanged += OnCompanionVisibilityChanged;
    }

    public void SetTaskState(AggregateTaskState state)
    {
        if (_disposed) return;
        string? next = Describe(state);
        if (!string.Equals(next, _message, StringComparison.Ordinal))
        {
            _message = next;
            _label.Text = next ?? string.Empty;
            UpdateBubbleSize();
        }
        SyncVisibility();
    }

    internal void ApplySettings(LumiSettings settings)
    {
        if (_disposed) return;
        _scale.ScaleX = _scale.ScaleY = Math.Clamp(settings.TaskBubbleScalePercent,
            LumiSettings.PanelScaleMinimumPercent, LumiSettings.PanelScaleMaximumPercent) / 100d;
        _elevation = Math.Clamp(settings.TaskBubbleElevation, -LumiSettings.PanelElevationLimit, LumiSettings.PanelElevationLimit);
        UpdateBubbleSize();
        SyncVisibility();
    }

    private void UpdateBubbleSize()
    {
        _label.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
        double width = Math.Clamp(Math.Ceiling(_label.DesiredSize.Width) + 2, DefaultWidth, MaxBubbleWidth);
        _label.Measure(new System.Windows.Size(width - 2, double.PositiveInfinity));
        double height = Math.Max(30, Math.Ceiling(_label.DesiredSize.Height) + 2);
        Width = Math.Ceiling(width * _scale.ScaleX);
        Height = Math.Ceiling(height * _scale.ScaleY);
    }

    internal static string? Describe(AggregateTaskState state)
    {
        var lines = new List<string>();
        bool confirm = false;
        foreach (var (source, progress) in SourceProgress(state).OrderBy(entry => entry.Key switch
        {
            LocalCodexTaskSource.SourceId => 0,
            LocalWorkBuddyTaskSource.SourceId => 1,
            _ => 2
        }).ThenBy(entry => entry.Key, StringComparer.Ordinal))
        {
            string? description = DescribeProgress(progress, out bool confirmSource);
            if (description is null) continue;
            string label = source switch
            {
                LocalCodexTaskSource.SourceId => "Codex",
                LocalWorkBuddyTaskSource.SourceId => "WorkBuddy",
                _ => source
            };
            lines.Add($"{label} · {description}");
            confirm |= confirmSource;
        }
        if (confirm) lines.Add("点击角色确认");
        return lines.Count == 0 ? null : string.Join("\n", lines);
    }

    private static IEnumerable<KeyValuePair<string, ConversationProgress>> SourceProgress(AggregateTaskState state)
    {
        if (state.ProgressBySource is { } sources) return sources;

        ConversationProgress progress = state.Progress ?? new ConversationProgress(Math.Max(1, state.KnownCount),
            state.State == AgentTaskState.Running ? Math.Max(1, state.RunningCount) : state.RunningCount,
            state.State == AgentTaskState.Review ? 1 : 0, state.State == AgentTaskState.Completed ? 1 : 0,
            state.State == AgentTaskState.Waiting ? Math.Max(1, state.WaitingCount) : state.WaitingCount,
            state.State == AgentTaskState.Failed ? 1 : 0, state.State == AgentTaskState.Unknown ? 1 : 0,
            state.State == AgentTaskState.Idle ? 1 : 0);
        if (state.WorkBuddyProgress is { } workBuddy)
            return [new(LocalCodexTaskSource.SourceId, progress), new(LocalWorkBuddyTaskSource.SourceId, workBuddy)];
        return [new(LocalCodexTaskSource.SourceId, progress)];
    }

    private static string? DescribeProgress(ConversationProgress progress, out bool confirm)
    {
        confirm = false;
        if (progress.TotalCount == 0 || (progress.ProcessingCount == 0 && progress.CompletedCount == 0 &&
            progress.WaitingCount == 0 && progress.FailedCount == 0 &&
            (!progress.IsCurrentBatch || progress.UnknownCount == 0))) return null;
        if (progress.AllCompleted)
        {
            confirm = true;
            return $"本批 {progress.CompletedCount} 轮已完成";
        }

        var lines = new List<string>();
        if (progress.ProcessingCount > 0) lines.Add(ProcessingText(progress.ProcessingCount));
        if (progress.CompletedCount > 0 || (progress.ProcessingCount > 0 && progress.TotalCount > 1))
            lines.Add(CompletedText(progress.CompletedCount));
        var attention = new List<string>();
        if (progress.WaitingCount > 0) attention.Add($"{progress.WaitingCount} 条等你确认");
        if (progress.FailedCount > 0) attention.Add(FailedText(progress.FailedCount));
        if (progress.UnknownCount > 0) attention.Add($"{progress.UnknownCount} 条状态待核");
        if (progress.InactiveCount > 0) attention.Add($"{progress.InactiveCount} 条未完成");
        if (attention.Count > 0) lines.Add(string.Join(" · ", attention));
        confirm = progress.ProcessingCount == 0 && progress.WaitingCount == 0 && progress.UnknownCount == 0 &&
            (progress.CompletedCount > 0 || progress.FailedCount > 0);
        return string.Join("\n", lines);
    }

    private static string ProcessingText(int count) => $"正在处理 {count} 条对话";
    private static string CompletedText(int count) => $"已完成 {count} 轮";
    private static string FailedText(int count) => $"{count} 轮遇到问题";

    /// <summary>All values are physical screen pixels; never clamp a bubble into the character.</summary>
    internal static Rect? PlanPlacement(Point head, Rect canvas, Rect workArea, System.Windows.Size bubble, double elevation = 0)
    {
        if (canvas.IsEmpty || bubble.Width <= 0 || bubble.Height <= 0 ||
            bubble.Width > workArea.Width || bubble.Height > workArea.Height) return null;
        const double gap = 8;
        double centered = head.X - bubble.Width / 2;
        (Point Anchor, bool Above)[] candidates =
        [
            (new Point(centered, head.Y - bubble.Height - gap), true),
            (new Point(centered, canvas.Bottom + gap), false),
            (new Point(canvas.Right + gap, head.Y), false),
            (new Point(canvas.Left - bubble.Width - gap, head.Y), false)
        ];

        return Fit(elevation) ?? (elevation == 0 ? null : Fit(0));

        Rect? Fit(double offset)
        {
            foreach ((Point anchor, bool above) in candidates)
            {
                var placed = new Rect(
                    Math.Clamp(anchor.X, workArea.Left, workArea.Right - bubble.Width),
                    Math.Clamp(anchor.Y - offset, workArea.Top, workArea.Bottom - bubble.Height), bubble.Width, bubble.Height);
                if (above ? placed.Bottom >= head.Y : placed.IntersectsWith(canvas)) continue;
                return placed;
            }
            return null;
        }
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        _handle = new WindowInteropHelper(this).Handle;
        NativeMethods.SetClickThrough(_handle, true);
        long style = NativeMethods.GetWindowLongPtr(_handle, NativeMethods.GWL_EXSTYLE).ToInt64();
        NativeMethods.SetWindowLongPtr(_handle, NativeMethods.GWL_EXSTYLE, new IntPtr(style | NoActivateStyle | 0x80L));
    }

    private void SyncVisibility()
    {
        if (_disposed) return;
        if (string.IsNullOrWhiteSpace(_message) || !_companion.IsVisible)
        {
            _placementTimer.Stop();
            if (IsVisible) Hide();
            return;
        }
        Topmost = _companion.Topmost;
        if (_handle == IntPtr.Zero) new WindowInteropHelper(this).EnsureHandle();
        if (!PlaceBubble())
        {
            if (IsVisible) Hide();
            _placementTimer.Start();
            return;
        }
        if (!IsVisible) Show();
        _placementTimer.Start();
    }

    private bool PlaceBubble()
    {
        if (_handle == IntPtr.Zero || !_companion.TryGetPanelLayout(out var layout) ||
            !NativeMethods.GetWindowRect(_handle, out var bubble)) return false;
        double elevation = _elevation * layout.Dpi.DpiScaleY;
        Rect? placement = PlanPlacement(layout.HeadAnchor, layout.Canvas, layout.WorkArea,
            new System.Windows.Size(bubble.Width, bubble.Height), elevation);
        if (placement is not { } chosen) return false;
        int left = (int)Math.Round(chosen.Left), top = (int)Math.Round(chosen.Top);
        return (bubble.Left == left && bubble.Top == top) || NativeMethods.SetWindowPos(_handle, IntPtr.Zero, left, top, 0, 0,
            NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);
    }

    private void OnPlacementTimer(object? sender, EventArgs e) => SyncVisibility();
    private void OnCompanionPlacementChanged(object? sender, EventArgs e) => SyncVisibility();
    private void OnCompanionVisibilityChanged(object sender, DependencyPropertyChangedEventArgs e) => SyncVisibility();

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_allowClose) return;
        e.Cancel = true;
        _placementTimer.Stop();
        Hide();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _placementTimer.Stop();
        _placementTimer.Tick -= OnPlacementTimer;
        _companion.LocationChanged -= OnCompanionPlacementChanged;
        _companion.SizeChanged -= OnCompanionPlacementChanged;
        _companion.IsVisibleChanged -= OnCompanionVisibilityChanged;
        SourceInitialized -= OnSourceInitialized;
        Closing -= OnClosing;
        _allowClose = true;
        Close();
    }
}
