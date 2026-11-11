// Version: 0.3.14-preview | Generated: 2026-11-10 | Updated/coverage: 2026-11-11 (Asia/Shanghai)
// Source: RAW | user dock/drag/return, automatic placement, two progress modes, links and perimeter progress; LOCAL-WORK | Codex Desktop / Lumi / Codex
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using LumiCompanion.Infrastructure;
using LumiCompanion.Models;
using LumiCompanion.Native;
using Controls = System.Windows.Controls;
using Forms = System.Windows.Forms;

namespace LumiCompanion.UI;

public sealed class ComfyUiStatusWindow : Window
{
    private const string ComfyUiAddress = "http://127.0.0.1:8188/";
    private readonly CompanionWindow _companion;
    private readonly CodexUsageBadgeWindow? _usageBadge;
    private readonly ComfyUiStatusService _statusService = new();
    private readonly ComfyUiProgressMonitor _progressMonitor = new();
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Controls.TextBlock _queue = new() { FontSize = 14, Text = "正在读取 ComfyUI…" };
    private readonly Controls.TextBlock _history = new() { FontSize = 12 };
    private readonly Controls.TextBlock _progress = new() { FontSize = 12, TextWrapping = TextWrapping.Wrap };
    private readonly Controls.TextBlock _status = new()
    {
        FontSize = 10, Foreground = UsageControlStyle.Muted, TextWrapping = TextWrapping.Wrap
    };
    private readonly ComfyUiPerimeterProgress _perimeterProgress = new() { IsHitTestVisible = false };
    private readonly Controls.Button _modeButton;
    private readonly Controls.Button _returnButton;
    private ComfyUiPanelPlacement _placement;
    private ComfyUiStatusSnapshot? _snapshot;
    private ComfyUiMonitorRead? _monitor;
    private bool _started;
    private bool _shuttingDown;
    private bool _placing;
    private bool _dragging;
    private bool _usageRepositionQueued;

    public ComfyUiStatusWindow(CompanionWindow companion, ComfyUiPanelPlacement placement,
        CodexUsageBadgeWindow? usageBadge = null)
    {
        _companion = companion;
        _usageBadge = usageBadge;
        _placement = placement;
        if (!ValidPoint(placement.Left, placement.Top))
            _placement = placement with { Docked = true };

        Title = "Lumi · ComfyUI";
        Width = 286;
        SizeToContent = SizeToContent.Height;
        WindowStyle = WindowStyle.None;
        WindowStartupLocation = WindowStartupLocation.Manual;
        ResizeMode = ResizeMode.NoResize;
        ShowActivated = false;
        ShowInTaskbar = false;
        Background = UsageControlStyle.Surface;
        Foreground = Brushes.White;

        var panel = new Controls.StackPanel { Margin = new Thickness(12, 9, 12, 10) };
        var header = new Controls.DockPanel();
        var hide = Button("×", "隐藏窗口", (_, _) => SetPlacement(_placement with { Hidden = true }));
        _returnButton = Button("归位", "贴回 Lumi", (_, _) =>
            SetPlacement(_placement with { Docked = true, Left = null, Top = null, Hidden = false }, reserveRightOnReturn: true));
        Controls.DockPanel.SetDock(hide, Controls.Dock.Right);
        Controls.DockPanel.SetDock(_returnButton, Controls.Dock.Right);
        header.Children.Add(hide);
        header.Children.Add(_returnButton);
        var title = new Controls.TextBlock
        {
            Text = "ComfyUI", FontSize = 14, FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center, Background = Brushes.Transparent,
            ToolTip = "拖动标题可独立摆放", Cursor = System.Windows.Input.Cursors.SizeAll
        };
        title.MouseLeftButtonDown += OnDragHeader;
        header.Children.Add(title);
        panel.Children.Add(header);

        _queue.Margin = new Thickness(0, 8, 0, 4);
        panel.Children.Add(_queue);
        panel.Children.Add(_progress);
        _history.Margin = new Thickness(0, 7, 0, 4);
        panel.Children.Add(_history);
        panel.Children.Add(_status);

        var actions = new Controls.StackPanel { Orientation = Controls.Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
        _modeButton = Button("", "切换真实节点步数与队列统计", (_, _) =>
            SetPlacement(_placement with { ShowNodeSteps = !_placement.ShowNodeSteps }));
        actions.Children.Add(_modeButton);
        actions.Children.Add(Button("打开输出", "打开 ComfyUI 输出目录，按住 Shift 可重新选择",
            async (_, _) => await OpenOutputAsync()));
        panel.Children.Add(actions);
        var address = new Controls.TextBlock { FontSize = 11, Margin = new Thickness(0, 4, 0, 0) };
        var link = new Hyperlink(new Run("127.0.0.1:8188 · 打开操作页面"))
        {
            NavigateUri = new Uri(ComfyUiAddress),
            Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(140, 213, 240))
        };
        link.Click += (_, _) => OpenTarget(ComfyUiAddress);
        address.Inlines.Add(link);
        panel.Children.Add(address);
        var surface = new Controls.Grid { Background = UsageControlStyle.Surface };
        surface.Children.Add(new Controls.Border
        {
            BorderBrush = UsageControlStyle.Muted,
            BorderThickness = new Thickness(1),
            Child = panel
        });
        surface.Children.Add(_perimeterProgress);
        Content = surface;

        Loaded += OnLoaded;
        SourceInitialized += (_, _) => Reposition();
        SizeChanged += OnPlacementChanged;
        _companion.LocationChanged += OnPlacementChanged;
        _companion.SizeChanged += OnPlacementChanged;
        _companion.IsVisibleChanged += OnCompanionVisibilityChanged;
        if (_usageBadge is not null)
        {
            ObserveUsagePlacement(_usageBadge, subscribe: true);
            ObserveUsagePlacement(_usageBadge.DockWindow, subscribe: true);
        }
        Render();
    }

    public event Action<ComfyUiPanelPlacement>? PlacementChanged;

    public void SyncVisibility() => SyncVisibility(reserveRightOnReturn: false);

    private void SyncVisibility(bool reserveRightOnReturn)
    {
        if (_shuttingDown) return;
        if (_placement.Hidden || !_companion.IsVisible)
        {
            Hide();
            return;
        }

        if (!IsVisible) Show();
        Reposition(reserveRightOnReturn);
    }

    public void Reveal()
    {
        SetPlacement(_placement with { Hidden = false });
    }

    public void Shutdown()
    {
        _shuttingDown = true;
        Close();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        if (_shuttingDown) return;
        e.Cancel = true;
        SetPlacement(_placement with { Hidden = true });
    }

    protected override void OnClosed(EventArgs e)
    {
        _companion.LocationChanged -= OnPlacementChanged;
        _companion.SizeChanged -= OnPlacementChanged;
        _companion.IsVisibleChanged -= OnCompanionVisibilityChanged;
        if (_usageBadge is not null)
        {
            ObserveUsagePlacement(_usageBadge, subscribe: false);
            ObserveUsagePlacement(_usageBadge.DockWindow, subscribe: false);
        }
        _lifetime.Cancel();
        if (!_started) DisposeConnection();
        base.OnClosed(e);
    }

    private void SetPlacement(ComfyUiPanelPlacement placement, bool reserveRightOnReturn = false)
    {
        _placement = placement;
        PlacementChanged?.Invoke(placement);
        Render();
        SyncVisibility(reserveRightOnReturn);
    }

    private void OnCompanionVisibilityChanged(object sender, DependencyPropertyChangedEventArgs e) => SyncVisibility();
    private void OnPlacementChanged(object? sender, EventArgs e) => Reposition();

    private void ObserveUsagePlacement(Window usage, bool subscribe)
    {
        if (subscribe)
        {
            usage.LocationChanged += OnUsagePlacementChanged;
            usage.SizeChanged += OnUsagePlacementChanged;
            usage.IsVisibleChanged += OnUsageVisibilityChanged;
        }
        else
        {
            usage.LocationChanged -= OnUsagePlacementChanged;
            usage.SizeChanged -= OnUsagePlacementChanged;
            usage.IsVisibleChanged -= OnUsageVisibilityChanged;
        }
    }

    private void OnUsagePlacementChanged(object? sender, EventArgs e) => QueueUsageReposition();

    private void OnUsageVisibilityChanged(object sender, DependencyPropertyChangedEventArgs e) => QueueUsageReposition();

    private void QueueUsageReposition()
    {
        if (!_placement.Docked || _usageRepositionQueued || _shuttingDown) return;
        _usageRepositionQueued = true;
        Dispatcher.InvokeAsync(() =>
        {
            _usageRepositionQueued = false;
            if (_placement.Docked) Reposition();
        }, DispatcherPriority.Loaded);
    }

    private void OnDragHeader(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) return;
        var handle = new WindowInteropHelper(this).Handle;
        if (!NativeMethods.GetWindowRect(handle, out var before)) return;

        _dragging = true;
        try { DragMove(); }
        catch (InvalidOperationException) { return; }
        finally { _dragging = false; }
        if (!NativeMethods.GetWindowRect(handle, out var after)) return;
        if (Math.Abs(after.Left - before.Left) > 2 || Math.Abs(after.Top - before.Top) > 2)
            SetPlacement(_placement with { Docked = false, Left = after.Left, Top = after.Top });
    }

    private void Reposition(bool reserveRightOnReturn = false)
    {
        if (_placing || _dragging || _shuttingDown || _placement.Hidden || !_companion.IsVisible) return;
        var handle = new WindowInteropHelper(this).Handle;
        var petHandle = _companion.HwndSource?.Handle ?? IntPtr.Zero;
        if (handle == IntPtr.Zero || petHandle == IntPtr.Zero ||
            !NativeMethods.GetWindowRect(petHandle, out var pet)) return;

        _placing = true;
        try
        {
            DpiScale dpi = VisualTreeHelper.GetDpi(this);
            int width = (int)Math.Ceiling(Width * dpi.DpiScaleX);
            int height = (int)Math.Ceiling(Math.Max(ActualHeight, 140) * dpi.DpiScaleY);
            const uint flags = NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE;
            if (_placement.Docked)
            {
                var screen = Forms.Screen.FromHandle(petHandle).WorkingArea;
                var workArea = new Rect(screen.Left, screen.Top, screen.Width, screen.Height);
                var petArea = new Rect(pet.Left, pet.Top, pet.Width, pet.Height);
                var panelSize = new System.Windows.Size(width, height);
                if (reserveRightOnReturn)
                {
                    double petLeft = UsageDockLayout.FitPetBesideComfyUi(petArea, workArea, panelSize);
                    if (petLeft < petArea.Left &&
                        NativeMethods.SetWindowPos(petHandle, IntPtr.Zero, (int)petLeft, pet.Top, 0, 0, flags))
                        petArea.X = petLeft;
                }

                Rect dock = UsageDockLayout.FindComfyUiPanel(petArea, workArea, panelSize, VisibleUsageBounds());
                NativeMethods.SetWindowPos(handle, IntPtr.Zero, (int)Math.Round(dock.Left), (int)Math.Round(dock.Top), 0, 0, flags);
                return;
            }

            int anchorX = (int)_placement.Left!.Value;
            int anchorY = (int)_placement.Top!.Value;
            var work = Forms.Screen.FromPoint(new System.Drawing.Point(anchorX, anchorY)).WorkingArea;
            int left = Math.Clamp(anchorX, work.Left, Math.Max(work.Left, work.Right - width));
            int top = Math.Clamp(anchorY, work.Top, Math.Max(work.Top, work.Bottom - height));
            NativeMethods.SetWindowPos(handle, IntPtr.Zero, left, top, 0, 0, flags);
        }
        finally { _placing = false; }
    }

    private Rect? VisibleUsageBounds()
    {
        if (_usageBadge is null) return null;
        Window usage = _usageBadge.IsExpanded ? _usageBadge : _usageBadge.DockWindow;
        if (!usage.IsVisible ||
            !NativeMethods.GetWindowRect(new WindowInteropHelper(usage).Handle, out var bounds)) return null;
        return new Rect(bounds.Left, bounds.Top, bounds.Width, bounds.Height);
    }

    private static bool ValidPoint(double? x, double? y) =>
        x is >= int.MinValue and <= int.MaxValue && y is >= int.MinValue and <= int.MaxValue;

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        _started = true;
        Loaded -= OnLoaded;
        CancellationToken token = _lifetime.Token;
        try
        {
            await Task.WhenAll(ReadStatusAsync(token), ReadNodeStepsAsync(token), ListenAsync(token));
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        finally { DisposeConnection(); }
    }

    private async Task ReadStatusAsync(CancellationToken token)
    {
        while (true)
        {
            if (IsVisible)
            {
                ComfyUiStatusSnapshot snapshot = await _statusService.ReadAsync(token);
                token.ThrowIfCancellationRequested();
                ApplyStatus(snapshot, _monitor);
            }
            await Task.Delay(TimeSpan.FromSeconds(IsVisible ? 5 : 1), token);
        }
    }

    private async Task ReadNodeStepsAsync(CancellationToken token)
    {
        while (true)
        {
            if (IsVisible && _placement.ShowNodeSteps)
            {
                ComfyUiMonitorRead? monitor = await _statusService.ReadMonitorAsync(token);
                token.ThrowIfCancellationRequested();
                ApplyStatus(_snapshot, monitor);
            }
            await Task.Delay(TimeSpan.FromSeconds(_monitor is null ? 3 : 1), token);
        }
    }

    private async Task ListenAsync(CancellationToken token)
    {
        while (true)
        {
            if (IsVisible && _placement.ShowNodeSteps)
                await _progressMonitor.ListenAsync(token);
            await Task.Delay(TimeSpan.FromSeconds(3), token);
        }
    }

    internal void ApplyStatus(ComfyUiStatusSnapshot? snapshot, ComfyUiMonitorRead? monitor)
    {
        _snapshot = snapshot;
        _monitor = monitor;
        Render();
    }

    private void Render()
    {
        _modeButton.Content = _placement.ShowNodeSteps ? "模式：节点步数" : "模式：队列统计";
        _returnButton.IsEnabled = !_placement.Docked;
        if (_snapshot is { } snapshot)
        {
            _queue.Text = $"排队 {Count(snapshot.Pending)}　运行 {Count(snapshot.Running)}";
            _history.Text = $"成功 {Count(snapshot.Succeeded)}　失败/中断 {Count(snapshot.Failed)}　输出 {Count(snapshot.SavedOutputs)}";
            _status.Text = $"{snapshot.CapturedAt.LocalDateTime:HH:mm:ss} · 近期历史最多 {ComfyUiStatusService.HistoryLimit} 条";
            if (!snapshot.Pending.HasValue || !snapshot.Succeeded.HasValue) _status.Text += $"\n{snapshot.Status}";
        }

        _progress.Visibility = _placement.ShowNodeSteps ? Visibility.Visible : Visibility.Collapsed;
        if (!_placement.ShowNodeSteps || _snapshot?.Running is not > 0)
        {
            _perimeterProgress.Fraction = 0;
            _progress.Text = _snapshot?.Running == 0 ? "当前空闲" : "";
            return;
        }

        var runningIds = _snapshot.RunningPromptIds;
        ComfyUiNodeProgress? node = _monitor?.Progress is { } current && runningIds.Contains(current.PromptId)
            ? current : _progressMonitor.Read(runningIds);
        if (node?.Value is double value && double.IsFinite(value) &&
            node.Maximum is double maximum && double.IsFinite(maximum) && maximum > 0)
        {
            _progress.Text = $"节点 {node.Node ?? "—"}　{value:0.##} / {maximum:0.##} 步";
            _perimeterProgress.Fraction = Math.Clamp(value / maximum, 0, 1);
            return;
        }

        _perimeterProgress.Fraction = 0;
        _progress.Text = node?.Node is { Length: > 0 } nodeId ? $"节点 {nodeId} 执行中"
            : _monitor is null ? "执行中 · 真实步数桥未接入" : "执行中 · 当前节点尚未报告步数";
    }

    private async Task OpenOutputAsync()
    {
        CancellationToken token = _lifetime.Token;
        bool chooseDirectory = (System.Windows.Input.Keyboard.Modifiers & ModifierKeys.Shift) != 0;
        string? directory = _placement.OutputDirectory ?? _monitor?.OutputDirectory;
        try
        {
            if (!chooseDirectory && !IsUsableDirectory(directory))
            {
                var monitor = await _statusService.ReadMonitorAsync(token);
                token.ThrowIfCancellationRequested();
                directory = monitor?.OutputDirectory ?? _placement.OutputDirectory;
            }
            if (chooseDirectory || !IsUsableDirectory(directory))
            {
                using var picker = new Forms.FolderBrowserDialog
                {
                    Description = "选择本机 ComfyUI 的输出目录",
                    UseDescriptionForTitle = true,
                    ShowNewFolderButton = false
                };
                if (picker.ShowDialog() != Forms.DialogResult.OK) return;
                directory = picker.SelectedPath;
                SetPlacement(_placement with { OutputDirectory = directory });
            }
            OpenTarget(directory!);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
    }

    private static bool IsUsableDirectory(string? directory) =>
        directory is not null && Path.IsPathFullyQualified(directory) && Directory.Exists(directory);

    private void OpenTarget(string target)
    {
        try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); }
        catch (Exception error) when (error is Win32Exception or InvalidOperationException)
        {
            _status.Text = "打开失败，请检查浏览器或输出目录。";
        }
    }

    private void DisposeConnection()
    {
        _progressMonitor.Dispose();
        _statusService.Dispose();
        _lifetime.Dispose();
    }

    private static Controls.Button Button(string text, string tooltip, RoutedEventHandler handler)
    {
        var button = new Controls.Button
        {
            Content = text, ToolTip = tooltip, Style = UsageControlStyle.Button,
            Margin = new Thickness(2, 0, 2, 0)
        };
        button.Click += handler;
        return button;
    }

    private static string Count(int? count) => count?.ToString() ?? "—";
}
