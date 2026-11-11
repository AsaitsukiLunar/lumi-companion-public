/*
Version: 0.3.14-preview
Generated: 2026-10-30; updated / coverage: 2026-11-11 (Asia/Shanghai)
Source type: RAW (user task bubble and shared confirmation rules); LOCAL-WORK (implementation); LOCAL-AGENT (WorkBuddy state bridge handoff V1.0).
Source: Codex Desktop / 云端本地互通文件夹 / current Lumi conversation (title unverified) / usage_tabs baseline; workbuddy_verify conversation-count increment
Organizing node: Windows / Codex Desktop / Codex
Original sources: current user requirement; existing account-scoped effective task state and sprite body mask.
*/
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using LumiCompanion.Animation;
using LumiCompanion.Integration;
using LumiCompanion.Native;
using Controls = System.Windows.Controls;
using MediaColor = System.Windows.Media.Color;

namespace LumiCompanion.UI;

/// <summary>Non-interactive high-level status only. No task text, reasoning, network or account access.</summary>
public sealed class TaskStatusBubbleWindow : Window, IDisposable
{
    internal const long NoActivateStyle = 0x08000000L;
    private const double DefaultWidth = 178;
    private const double MaxWorkBuddyWidth = 216;
    private readonly CompanionWindow _companion;
    private readonly CodexUsageBadgeWindow? _usageBadge;
    private readonly Controls.TextBlock _label;
    private readonly DispatcherTimer _placementTimer;
    private readonly Dictionary<SpriteHitMask, Rect> _bodyBounds = new();
    private string? _message;
    private bool _disposed;
    private bool _allowClose;
    private IntPtr _handle;

    public TaskStatusBubbleWindow(CompanionWindow companion, CodexUsageBadgeWindow? usageBadge = null)
    {
        _companion = companion;
        _usageBadge = usageBadge;
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
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Child = _label
        };
        _placementTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(100) };
        _placementTimer.Tick += OnPlacementTimer;
        SourceInitialized += OnSourceInitialized;
        Closing += OnClosing;
        _companion.LocationChanged += OnCompanionPlacementChanged;
        _companion.SizeChanged += OnCompanionPlacementChanged;
        _companion.IsVisibleChanged += OnCompanionVisibilityChanged;
        if (_usageBadge is not null)
        {
            _usageBadge.LocationChanged += OnCompanionPlacementChanged;
            _usageBadge.SizeChanged += OnCompanionPlacementChanged;
            _usageBadge.IsVisibleChanged += OnCompanionVisibilityChanged;
        }
    }

    public void SetTaskState(AggregateTaskState state)
    {
        if (_disposed) return;
        string? next = Describe(state);
        if (!string.Equals(next, _message, StringComparison.Ordinal))
        {
            _message = next;
            _label.Text = next ?? string.Empty;
            if (state.WorkBuddyProgress is not { TotalCount: > 0 })
            {
                Width = DefaultWidth;
            }
            else
            {
                _label.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
                Width = Math.Clamp(Math.Ceiling(_label.DesiredSize.Width) + 2, DefaultWidth, MaxWorkBuddyWidth);
            }
            _label.Measure(new System.Windows.Size(Width - 2, double.PositiveInfinity));
            Height = Math.Max(30, Math.Ceiling(_label.DesiredSize.Height) + 2);
        }
        SyncVisibility();
    }

    internal static string? Describe(AggregateTaskState state)
    {
        ConversationProgress progress = state.Progress ?? new ConversationProgress(Math.Max(1, state.KnownCount),
            state.State == AgentTaskState.Running ? Math.Max(1, state.RunningCount) : state.RunningCount,
            state.State == AgentTaskState.Review ? 1 : 0, state.State == AgentTaskState.Completed ? 1 : 0,
            state.State == AgentTaskState.Waiting ? Math.Max(1, state.WaitingCount) : state.WaitingCount,
            state.State == AgentTaskState.Failed ? 1 : 0, state.State == AgentTaskState.Unknown ? 1 : 0,
            state.State == AgentTaskState.Idle ? 1 : 0);
        string? codex = DescribeProgress(progress, out bool confirmCodex);
        bool confirmWorkBuddy = false;
        string? workBuddy = state.WorkBuddyProgress is { } sourceProgress
            ? DescribeProgress(sourceProgress, out confirmWorkBuddy) : null;
        string? text = workBuddy is null ? codex
            : codex is null ? $"WorkBuddy · {workBuddy}"
            : $"Codex · {codex}\nWorkBuddy · {workBuddy}";
        return confirmCodex || confirmWorkBuddy ? text + "\n点击角色确认" : text;
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

    internal static Rect FindBodyBounds(SpriteHitMask mask)
    {
        int left = mask.Width, top = mask.Height, right = -1, bottom = -1;
        for (int y = 0; y < mask.Height; y++)
            for (int x = 0; x < mask.Width; x++)
                if (mask.GetPart(x, y) == SpriteHitPart.Body)
                {
                    left = Math.Min(left, x); top = Math.Min(top, y);
                    right = Math.Max(right, x); bottom = Math.Max(bottom, y);
                }
        return right < left ? Rect.Empty : new Rect(left, top, right - left + 1, bottom - top + 1);
    }

    /// <summary>All values are physical screen pixels; never clamp a bubble into the character.</summary>
    internal static Rect? PlanPlacement(Rect body, Rect workArea, System.Windows.Size bubble, Rect? badge)
    {
        if (body.IsEmpty || bubble.Width <= 0 || bubble.Height <= 0 ||
            bubble.Width > workArea.Width || bubble.Height > workArea.Height) return null;
        const double gap = 8;
        double centered = body.Left + (body.Width - bubble.Width) / 2;
        var candidates = new List<Point>
        {
            new Point(centered, body.Top - bubble.Height - gap),
            new Point(body.Right + gap, body.Top),
            new Point(body.Left - bubble.Width - gap, body.Top)
        };
        if (badge is { } card)
        {
            candidates.Add(new Point(Math.Max(body.Right, card.Right) + gap, body.Top));
            candidates.Add(new Point(Math.Min(body.Left, card.Left) - bubble.Width - gap, body.Top));
        }
        foreach (Point candidate in candidates)
        {
            var placed = new Rect(
                Math.Clamp(candidate.X, workArea.Left, workArea.Right - bubble.Width),
                Math.Clamp(candidate.Y, workArea.Top, workArea.Bottom - bubble.Height), bubble.Width, bubble.Height);
            if (placed.IntersectsWith(body) || (badge is { } occupied && placed.IntersectsWith(occupied))) continue;
            return placed;
        }
        return null;
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
        SpriteHitMask? mask = _companion.CurrentHitMask;
        IntPtr companionHandle = _companion.HwndSource?.Handle ?? IntPtr.Zero;
        if (mask is null || companionHandle == IntPtr.Zero || _handle == IntPtr.Zero ||
            !NativeMethods.GetWindowRect(companionHandle, out var pet) ||
            !NativeMethods.GetWindowRect(_handle, out var bubble)) return false;
        if (!_bodyBounds.TryGetValue(mask, out Rect bodyBounds))
        {
            bodyBounds = FindBodyBounds(mask);
            _bodyBounds[mask] = bodyBounds;
        }
        if (bodyBounds.IsEmpty) return false;
        double scale = Math.Min(pet.Width / (double)mask.Width, pet.Height / (double)mask.Height);
        var body = new Rect(pet.Left + (pet.Width - mask.Width * scale) / 2 + bodyBounds.Left * scale,
            pet.Top + (pet.Height - mask.Height * scale) / 2 + bodyBounds.Top * scale,
            bodyBounds.Width * scale, bodyBounds.Height * scale);
        IntPtr monitor = NativeMethods.MonitorFromWindow(companionHandle, NativeMethods.MONITOR_DEFAULTTONEAREST);
        var info = new NativeMethods.MONITORINFO { cbSize = (uint)Marshal.SizeOf<NativeMethods.MONITORINFO>() };
        if (!NativeMethods.GetMonitorInfo(monitor, ref info)) return false;
        var work = new Rect(info.rcWork.Left, info.rcWork.Top, info.rcWork.Width, info.rcWork.Height);
        Rect? badge = null;
        if (_usageBadge is { IsVisible: true } && NativeMethods.GetWindowRect(new WindowInteropHelper(_usageBadge).Handle, out var badgeRect))
            badge = new Rect(badgeRect.Left, badgeRect.Top, badgeRect.Width, badgeRect.Height);
        Rect? placement = PlanPlacement(body, work, new System.Windows.Size(bubble.Width, bubble.Height), badge);
        if (placement is not { } chosen) return false;
        NativeMethods.SetWindowPos(_handle, IntPtr.Zero, (int)Math.Round(chosen.Left), (int)Math.Round(chosen.Top), 0, 0,
            NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);
        return true;
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
        if (_usageBadge is not null)
        {
            _usageBadge.LocationChanged -= OnCompanionPlacementChanged;
            _usageBadge.SizeChanged -= OnCompanionPlacementChanged;
            _usageBadge.IsVisibleChanged -= OnCompanionVisibilityChanged;
        }
        SourceInitialized -= OnSourceInitialized;
        Closing -= OnClosing;
        _allowClose = true;
        Close();
    }
}
