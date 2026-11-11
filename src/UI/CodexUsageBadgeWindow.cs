/*
Traceability metadata
Version: 0.3.14-preview
Generated: 2026-10-02 (Asia/Shanghai)
Last updated: 2026-11-11
Content coverage through: 2026-11-11
Source type: RAW (user multi-provider tab and quota reset time requests); LOCAL-WORK (implementation and local quota model)
Source: Codex Desktop / 云端本地互通文件夹 / current Lumi conversation (title unverified) / Codex
Organizing node: Windows / Codex Desktop / Codex
Original sources: existing Lumi side card; user numeric tabs, configurable dashboard links, and collapsible display requirements.
*/
using System.Diagnostics;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using System.Runtime.InteropServices;
using LumiCompanion.Infrastructure;
using LumiCompanion.Models;
using LumiCompanion.Native;
using Controls = System.Windows.Controls;
using Brush = System.Windows.Media.Brush;
using MediaColor = System.Windows.Media.Color;

namespace LumiCompanion.UI;

public sealed class CodexUsageBadgeWindow : Window, IDisposable
{
    private const double DefaultCardHeight = 184;
    private const double CardHeightWithResetTimes = 214;
    private readonly CompanionWindow _companion;
    private readonly UsageDockButtonWindow _dock;
    private Window? _statusBubble;
    private readonly ScaleTransform _popoverScale = new(1, 1);
    private int _animationToken;
    private bool _placing;
    private bool _companionPlacementQueued;
    private readonly ProviderTabsStore _tabsStore;
    private ProviderTabsDocument _tabs;
    private CodexUsageSnapshot _snapshot = CodexUsageSnapshot.Loading();
    private DateTimeOffset? _lastCheckedAt;
    private readonly Controls.TextBlock _title;
    private readonly Controls.StackPanel _body;
    private readonly Controls.StackPanel _tabButtons;
    private readonly Controls.StackPanel _codexBody;
    private readonly Controls.StackPanel _webBody;
    private readonly Controls.TextBlock _fiveHourValue;
    private readonly Controls.TextBlock _weeklyValue;
    private readonly Controls.ProgressBar _fiveHourBar;
    private readonly Controls.ProgressBar _weeklyBar;
    private readonly Controls.TextBlock _resetTimes;
    private readonly Controls.TextBlock _source;
    private readonly Controls.TextBlock _status;
    private readonly Controls.TextBlock _webStatus;
    private readonly Controls.Button _openPage;
    private readonly Controls.MenuItem _openDashboard;
    private readonly Controls.Button _collapseButton;
    private readonly DispatcherTimer _freshnessTimer;
    private string? _configurationProblem;
    private bool _enabled;
    private bool _showResetTimes;
    private bool _allowClose;
    private bool _collapsed = true;

    public CodexUsageBadgeWindow(CompanionWindow companion, ProviderTabsStore? tabsStore = null)
    {
        _companion = companion;
        _tabsStore = tabsStore ?? new ProviderTabsStore();
        _tabs = _tabsStore.Load(out _configurationProblem);
        Title = "Lumi · 用量标签";
        Owner = companion;
        Width = 224;
        Height = DefaultCardHeight;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = companion.Topmost;
        IsHitTestVisible = true;
        UseLayoutRounding = true;
        SnapsToDevicePixels = true;
        _dock = new UsageDockButtonWindow(companion);
        _dock.ToggleRequested += OnDockToggle;
        _dock.HideRequested += OnDockHide;

        var stack = new Controls.StackPanel { Margin = new Thickness(12, 9, 12, 10) };
        var header = new Controls.Grid();
        header.ColumnDefinitions.Add(new Controls.ColumnDefinition());
        header.ColumnDefinitions.Add(new Controls.ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new Controls.ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new Controls.ColumnDefinition { Width = GridLength.Auto });
        _title = Text("Codex", 11, Brushes.White);
        _title.FontWeight = FontWeights.SemiBold;
        _title.TextTrimming = TextTrimming.CharacterEllipsis;
        _title.VerticalAlignment = VerticalAlignment.Center;
        header.Children.Add(_title);
        var configure = SmallButton("⋯", "更多：打开用量页、编辑标签", (_, _) => { });
        var menu = new Controls.ContextMenu
        {
            Background = UsageControlStyle.Surface, Foreground = Brushes.White,
            BorderBrush = UsageControlStyle.Edge, BorderThickness = new Thickness(1), Padding = new Thickness(4)
        };
        // The system template paints its own light icon gutter even with a dark Background.
        var menuFrame = new FrameworkElementFactory(typeof(Controls.Border));
        menuFrame.SetValue(Controls.Border.BackgroundProperty, new TemplateBindingExtension(Controls.Control.BackgroundProperty));
        menuFrame.SetValue(Controls.Border.BorderBrushProperty, new TemplateBindingExtension(Controls.Control.BorderBrushProperty));
        menuFrame.SetValue(Controls.Border.BorderThicknessProperty, new TemplateBindingExtension(Controls.Control.BorderThicknessProperty));
        menuFrame.SetValue(Controls.Border.PaddingProperty, new TemplateBindingExtension(Controls.Control.PaddingProperty));
        var menuItems = new FrameworkElementFactory(typeof(Controls.ItemsPresenter));
        menuItems.SetValue(System.Windows.Input.KeyboardNavigation.DirectionalNavigationProperty,
            System.Windows.Input.KeyboardNavigationMode.Cycle);
        menuFrame.AppendChild(menuItems);
        menu.Template = new Controls.ControlTemplate(typeof(Controls.ContextMenu)) { VisualTree = menuFrame };
        _openDashboard = new Controls.MenuItem { Header = "打开用量页", Style = UsageControlStyle.MenuItem };
        _openDashboard.Click += (_, _) => OpenSelectedDashboard();
        var edit = new Controls.MenuItem { Header = "编辑标签…", Style = UsageControlStyle.MenuItem };
        edit.Click += (_, _) => EditTabs();
        menu.Items.Add(_openDashboard); menu.Items.Add(edit);
        configure.ContextMenu = menu;
        configure.Click += (_, _) => { menu.PlacementTarget = configure; menu.IsOpen = true; };
        Controls.Grid.SetColumn(configure, 1);
        header.Children.Add(configure);
        _collapseButton = SmallButton("−", "缩小到脚下", (_, _) => SetCollapsed(true));
        Controls.Grid.SetColumn(_collapseButton, 2);
        header.Children.Add(_collapseButton);
        var hide = SmallButton("×", "隐藏用量卡；从托盘恢复", (_, _) => OnDockHide());
        Controls.Grid.SetColumn(hide, 3); header.Children.Add(hide);
        stack.Children.Add(header);

        _body = new Controls.StackPanel { Margin = new Thickness(0, 5, 0, 0) };
        _tabButtons = new Controls.StackPanel { Orientation = Controls.Orientation.Horizontal };
        _body.Children.Add(new Controls.ScrollViewer
        {
            Content = _tabButtons,
            HorizontalScrollBarVisibility = Controls.ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = Controls.ScrollBarVisibility.Disabled,
            MaxHeight = 29,
            Margin = new Thickness(0, 1, 0, 9)
        });
        _codexBody = new Controls.StackPanel();
        _codexBody.Children.Add(CreateUsageRow("5 小时", out _fiveHourValue, out _fiveHourBar));
        _codexBody.Children.Add(CreateUsageRow("本周", out _weeklyValue, out _weeklyBar));
        _resetTimes = Text(string.Empty, 9, MutedBrush());
        _resetTimes.Margin = new Thickness(0, 2, 0, 0);
        _resetTimes.Visibility = Visibility.Collapsed;
        _codexBody.Children.Add(_resetTimes);
        _source = Text(string.Empty, 9, MutedBrush());
        _source.TextWrapping = TextWrapping.Wrap;
        _source.Margin = new Thickness(0, 5, 0, 0);
        _codexBody.Children.Add(_source);
        _status = Text(string.Empty, 9, MutedBrush());
        _status.TextWrapping = TextWrapping.Wrap;
        _status.MaxHeight = 28;
        _codexBody.Children.Add(_status);
        _body.Children.Add(_codexBody);

        _webBody = new Controls.StackPanel();
        _webStatus = Text(string.Empty, 10, MutedBrush());
        _webStatus.TextWrapping = TextWrapping.Wrap;
        _webStatus.Margin = new Thickness(0, 0, 0, 8);
        _webBody.Children.Add(_webStatus);
        _openPage = SmallButton("打开用量页", "在系统浏览器中查看", (_, _) => OpenSelectedDashboard());
        _openPage.HorizontalAlignment = HorizontalAlignment.Left;
        _webBody.Children.Add(_openPage);
        _body.Children.Add(_webBody);
        stack.Children.Add(_body);

        Content = new Controls.Border
        {
            Child = stack,
            CornerRadius = new CornerRadius(15),
            Background = UsageControlStyle.Surface,
            BorderBrush = UsageControlStyle.Edge,
            BorderThickness = new Thickness(1)
        };
        ((FrameworkElement)Content).RenderTransform = _popoverScale;
        ((FrameworkElement)Content).RenderTransformOrigin = new Point(.5, 0);
        _freshnessTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _freshnessTimer.Tick += OnFreshnessTimer;
        SourceInitialized += OnSourceInitialized;
        Closing += OnClosing;
        _companion.LocationChanged += OnCompanionPlacementChanged;
        _companion.SizeChanged += OnCompanionPlacementChanged;
        _companion.IsVisibleChanged += OnCompanionVisibilityChanged;
        RefreshTabs();
    }

    public void ApplySettings(LumiSettings settings)
    {
        _enabled = settings.ShowCodexUsage;
        _showResetTimes = settings.ShowCodexUsageResetTimes;
        if (!_enabled) _collapsed = true;
        Topmost = settings.AlwaysOnTop;
        SyncVisibility();
    }

    public void SetSnapshot(CodexUsageSnapshot snapshot, DateTimeOffset? checkedAt = null)
    {
        _snapshot = snapshot;
        if (checkedAt.HasValue) _lastCheckedAt = checkedAt;
        RenderSelectedTab();
    }

    internal DateTimeOffset? LastCheckedAt => _lastCheckedAt;

    public bool IsExpanded => !_collapsed;
    public event Action<bool>? ModuleVisibilityRequested;
    internal Window DockWindow => _dock;
    public void SetStatusBubble(Window bubble) { _statusBubble = bubble; SyncVisibility(); }
    public void SetExpanded(bool expanded) => SetCollapsed(!expanded);

    public bool ContainsScreenPoint(Point screenPoint)
    {
        if (_dock.IsVisible && Contains(_dock, screenPoint)) return true;
        if (!IsVisible || PresentationSource.FromVisual(this) is null) return false;
        Point local = PointFromScreen(screenPoint);
        return local.X >= 0 && local.Y >= 0 && local.X < ActualWidth && local.Y < ActualHeight;
    }

    private ProviderTab SelectedTab => _tabs.Tabs.First(tab => tab.Id == _tabs.SelectedTabId);

    private void RefreshTabs()
    {
        _tabButtons.Children.Clear();
        for (int index = 0; index < _tabs.Tabs.Count; index++)
        {
            ProviderTab tab = _tabs.Tabs[index];
            var button = SmallButton((index + 1).ToString(), tab.Name, (_, _) => SelectTab(tab.Id));
            button.MinWidth = 26;
            button.Margin = new Thickness(0, 0, 5, 0);
            if (tab.Id == _tabs.SelectedTabId)
            {
                button.Background = UsageControlStyle.Brush(48, 66, 73);
                button.Foreground = UsageControlStyle.Brush(172, 222, 229);
            }
            _tabButtons.Children.Add(button);
        }
        if (IsExpanded && IsVisible) SyncVisibility();
        else RenderSelectedTab();
    }

    private void SelectTab(string id)
    {
        _tabs.SelectedTabId = id;
        if (_configurationProblem is null) SaveTabs();
        RefreshTabs();
    }

    private void RenderSelectedTab()
    {
        ProviderTab tab = SelectedTab;
        int number = _tabs.Tabs.IndexOf(tab) + 1;
        _title.Text = $"{number} · {tab.Name}";
        _title.ToolTip = tab.Name;
        bool configured = ProviderTabsStore.TryGetDashboardUri(tab.EffectiveDashboardUrl, out _);
        _openDashboard.IsEnabled = configured;
        _codexBody.Visibility = tab.IsCodex ? Visibility.Visible : Visibility.Collapsed;
        _webBody.Visibility = tab.IsCodex ? Visibility.Collapsed : Visibility.Visible;
        bool showResetTimes = tab.IsCodex && _showResetTimes;
        _resetTimes.Visibility = showResetTimes ? Visibility.Visible : Visibility.Collapsed;
        Height = showResetTimes ? CardHeightWithResetTimes : DefaultCardHeight;
        string compactSummary;
        if (tab.IsCodex)
        {
            UsageBadgeProjection current = UsageBadgeProjection.At(_snapshot, DateTimeOffset.Now);
            RenderQuota(current.FiveHour, _fiveHourValue, _fiveHourBar, current.IsStale);
            RenderQuota(current.Weekly, _weeklyValue, _weeklyBar, current.IsStale);
            if (showResetTimes)
            {
                _resetTimes.Text = $"5 小时重置：{FormatResetTime(current.FiveHour)}\n本周重置：{FormatResetTime(current.Weekly)}";
            }
            _source.Text = _snapshot.HasAnyQuota
                ? $"本地观测 · 非实时\n{_snapshot.CapturedAt.LocalDateTime:MM-dd HH:mm} 采样" + (current.IsStale ? " · 旧快照" : "")
                : "本地观测 · 非实时";
            _status.Text = _configurationProblem ?? current.Status;
            ToolTip = new CodexUsageSnapshot(current.FiveHour, current.Weekly, _snapshot.CapturedAt, current.Status)
            {
                SourceLabel = _snapshot.SourceLabel, IsStale = current.IsStale,
                TodayTokens = _snapshot.TodayTokens, TodayStatus = _snapshot.TodayStatus
            }.BuildToolTip(showResetTimes) + Environment.NewLine +
                (_lastCheckedAt.HasValue ? $"上次检查 {_lastCheckedAt.Value.LocalDateTime:MM-dd HH:mm:ss}" : "上次检查：尚未执行") +
                Environment.NewLine + "检查本地记录；不会主动刷新服务器额度。";
            _source.ToolTip = ToolTip;
            _status.ToolTip = ToolTip;
            string Percent(CodexUsageWindow? window) => window is null ? "--" : window.RemainingPercent + "%";
            compactSummary = current.HasAnyQuota
                ? $"{(current.IsStale ? "旧 " : "余 ")}5h {Percent(current.FiveHour)} · 周 {Percent(current.Weekly)}"
                : "用量未知";
        }
        else
        {
            _webStatus.Text = _configurationProblem ?? (configured
                ? "用量页已配置\n点击下方按钮打开；尚未自动读取用量或积分。"
                : "网页入口 · 尚未自动读取用量\n请点右上角 … 配置用量页面链接。");
            _openPage.IsEnabled = configured;
            ToolTip = "套餐额度、API 用量和积分分别统计；此标签不会自动读取浏览器账户。";
            compactSummary = configured ? "网页入口已配置" : "请先配置用量网址";
        }
        _dock.SetSummary($"{number} · {tab.Name}", compactSummary, ToolTip);
    }

    private void EditTabs()
    {
        var editor = new ProviderTabsEditorWindow(this, _tabs);
        if (editor.ShowDialog() == true)
        {
            ProviderTabsDocument previous = _tabs;
            _tabs = editor.Result;
            if (!SaveTabs()) _tabs = previous;
            RefreshTabs();
        }
    }

    private bool SaveTabs()
    {
        try
        {
            _tabsStore.Save(_tabs);
            _configurationProblem = null;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _configurationProblem = "标签未能保存，请检查本地数据目录。";
            return false;
        }
    }

    private void OpenSelectedDashboard()
    {
        if (!ProviderTabsStore.TryGetDashboardUri(SelectedTab.EffectiveDashboardUrl, out Uri? uri)) return;
        try { Process.Start(new ProcessStartInfo(uri!.AbsoluteUri) { UseShellExecute = true }); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            _webStatus.Text = "页面暂时无法打开，请检查默认浏览器。";
        }
    }

    private void SetCollapsed(bool collapsed)
    {
        if (_collapsed == collapsed) return;
        _collapsed = collapsed;
        _dock.SetExpanded(!collapsed);
        if (collapsed)
        {
            if (_enabled && _companion.IsVisible && PlaceBesideCompanion()) _dock.Show();
            AnimateClosed();
        }
        else
        {
            bool reversingClose = IsVisible;
            SyncVisibility();
            if (reversingClose && IsVisible) AnimateOpened();
        }
    }

    private void OnDockToggle() => SetCollapsed(!_collapsed);
    private void OnDockHide()
    {
        _enabled = false;
        _collapsed = true;
        SyncVisibility();
        ModuleVisibilityRequested?.Invoke(false);
    }

    private void AnimateOpened()
    {
        ++_animationToken;
        var duration = new Duration(TimeSpan.FromMilliseconds(180));
        BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, duration));
        _popoverScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(.96, 1, duration));
        _popoverScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(.96, 1, duration));
    }

    private void AnimateClosed()
    {
        int token = ++_animationToken;
        if (!IsVisible) return;
        var duration = new Duration(TimeSpan.FromMilliseconds(160));
        var fade = new DoubleAnimation(Opacity, 0, duration);
        fade.Completed += (_, _) => { if (token == _animationToken && _collapsed) Hide(); };
        BeginAnimation(OpacityProperty, fade);
        _popoverScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1, .96, duration));
        _popoverScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1, .96, duration));
    }


    private void OnFreshnessTimer(object? sender, EventArgs e)
    {
        if ((IsVisible || _dock.IsVisible) && SelectedTab.IsCodex) RenderSelectedTab();
    }

    private static void RenderQuota(CodexUsageWindow? window, Controls.TextBlock value, Controls.ProgressBar bar, bool stale)
    {
        value.Text = window is null ? "--" : window.RemainingPercent + "%";
        bar.Value = window?.RemainingPercent ?? 0;
        bar.Visibility = window is null ? Visibility.Hidden : Visibility.Visible;
        bar.Opacity = stale ? 0.55 : 1;
    }

    private static string FormatResetTime(CodexUsageWindow? window) =>
        window?.ResetsAt is { } reset ? $"{reset.LocalDateTime:MM-dd HH:mm}" : "未知";

    private static Controls.Grid CreateUsageRow(string label, out Controls.TextBlock value, out Controls.ProgressBar bar)
    {
        var grid = new Controls.Grid { Margin = new Thickness(0, 3, 0, 5) };
        grid.ColumnDefinitions.Add(new Controls.ColumnDefinition { Width = new GridLength(43) });
        grid.ColumnDefinitions.Add(new Controls.ColumnDefinition());
        grid.ColumnDefinitions.Add(new Controls.ColumnDefinition { Width = new GridLength(34) });
        var name = Text(label, 10, MutedBrush());
        name.VerticalAlignment = VerticalAlignment.Center;
        grid.Children.Add(name);
        bar = new Controls.ProgressBar
        {
            Minimum = 0, Maximum = 100, Height = 5, Value = 0,
            Foreground = UsageControlStyle.Brush(139, 192, 202),
            Background = UsageControlStyle.Brush(53, 58, 70),
            BorderThickness = new Thickness(0), VerticalAlignment = VerticalAlignment.Center
        };
        Controls.Grid.SetColumn(bar, 1);
        grid.Children.Add(bar);
        value = Text("--", 10, Brushes.White);
        value.FontWeight = FontWeights.SemiBold;
        value.TextAlignment = TextAlignment.Right;
        value.VerticalAlignment = VerticalAlignment.Center;
        Controls.Grid.SetColumn(value, 2);
        grid.Children.Add(value);
        return grid;
    }

    private static Controls.TextBlock Text(string text, double size, Brush foreground) => new()
    {
        Text = text, FontSize = size, Foreground = foreground
    };

    private static SolidColorBrush MutedBrush() => UsageControlStyle.Brush(170, 177, 192);

    private static Controls.Button SmallButton(string text, string hint, RoutedEventHandler click)
    {
        var button = new Controls.Button
        {
            Content = text, ToolTip = hint, Style = UsageControlStyle.Button,
            Margin = new Thickness(1, 0, 1, 0)
        };
        button.Click += click;
        return button;
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        NativeMethods.SetClickThrough(new WindowInteropHelper(this).Handle, false);
        PlaceBesideCompanion();
    }

    private void OnCompanionPlacementChanged(object? sender, EventArgs e)
    {
        if (_statusBubble is not { IsVisible: true })
        {
            SyncVisibility();
            return;
        }

        // The bubble also follows Lumi. Let it move before using its bounds to place the card.
        if (_companionPlacementQueued) return;
        _companionPlacementQueued = true;
        Dispatcher.InvokeAsync(() =>
        {
            _companionPlacementQueued = false;
            if (!_allowClose) SyncVisibility();
        }, DispatcherPriority.Loaded);
    }

    private void OnCompanionVisibilityChanged(object sender, DependencyPropertyChangedEventArgs e) => SyncVisibility();

    private void SyncVisibility()
    {
        bool shouldShow = _enabled && _companion.IsVisible;
        if (shouldShow)
        {
            _dock.Topmost = Topmost;
            RenderSelectedTab();
            if (!PlaceBesideCompanion()) { _dock.Hide(); Hide(); return; }
            if (_collapsed)
            {
                if (!_dock.IsVisible) _dock.Show();
            }
            else _dock.Hide();
            _freshnessTimer.Start();
            if (!_collapsed && !IsVisible) { Show(); AnimateOpened(); }
        }
        else
        {
            _freshnessTimer.Stop();
            ++_animationToken;
            _dock.Hide();
            Hide();
        }
    }

    private bool PlaceBesideCompanion()
    {
        if (_placing || _companion.HwndSource is null) return false;
        _placing = true;
        try
        {
            IntPtr petHandle = _companion.HwndSource.Handle;
            if (!NativeMethods.GetWindowRect(petHandle, out var petBounds)) return false;
            var pet = new Rect(petBounds.Left, petBounds.Top, petBounds.Width, petBounds.Height);
            var info = new NativeMethods.MONITORINFO { cbSize = (uint)Marshal.SizeOf<NativeMethods.MONITORINFO>() };
            if (!NativeMethods.GetMonitorInfo(NativeMethods.MonitorFromWindow(petHandle, NativeMethods.MONITOR_DEFAULTTONEAREST), ref info)) return false;
            var work = new Rect(info.rcWork.Left, info.rcWork.Top, info.rcWork.Width, info.rcWork.Height);
            Rect? bubble = _statusBubble is { IsVisible: true } ? ScreenRect(_statusBubble) : null;
            DpiScale dpi = VisualTreeHelper.GetDpi(_companion);
            if (_collapsed)
            {
                Rect? dock = UsageDockLayout.FindSwitch(pet, work,
                    new System.Windows.Size(_dock.Width * dpi.DpiScaleX, _dock.Height * dpi.DpiScaleY), bubble);
                if (dock is not { } chosen) return false;
                IntPtr dockHandle = new WindowInteropHelper(_dock).EnsureHandle();
                NativeMethods.SetWindowPos(dockHandle, IntPtr.Zero, (int)Math.Round(chosen.Left), (int)Math.Round(chosen.Top),
                    (int)Math.Round(chosen.Width), (int)Math.Round(chosen.Height), NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);
            }
            else
            {
                Rect? card = UsageDockLayout.FindPopover(pet, work, Rect.Empty,
                    new System.Windows.Size(Width * dpi.DpiScaleX, Height * dpi.DpiScaleY), bubble);
                if (card is not { } placed) return false;
                IntPtr cardHandle = new WindowInteropHelper(this).EnsureHandle();
                NativeMethods.SetWindowPos(cardHandle, IntPtr.Zero, (int)Math.Round(placed.Left), (int)Math.Round(placed.Top),
                    (int)Math.Round(placed.Width), (int)Math.Round(placed.Height), NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);
            }
            return true;
        }
        finally { _placing = false; }
    }

    private static Rect ScreenRect(Window window)
    {
        NativeMethods.GetWindowRect(new WindowInteropHelper(window).Handle, out var rect);
        return new Rect(rect.Left, rect.Top, rect.Width, rect.Height);
    }

    private static bool Contains(Window window, Point point) => ScreenRect(window).Contains(point);

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_allowClose) return;
        e.Cancel = true;
        OnDockHide();
    }

    public void Dispose()
    {
        _freshnessTimer.Stop();
        _freshnessTimer.Tick -= OnFreshnessTimer;
        _dock.ToggleRequested -= OnDockToggle;
        _dock.HideRequested -= OnDockHide;
        _dock.Dispose();
        _companion.LocationChanged -= OnCompanionPlacementChanged;
        _companion.SizeChanged -= OnCompanionPlacementChanged;
        _companion.IsVisibleChanged -= OnCompanionVisibilityChanged;
        SourceInitialized -= OnSourceInitialized;
        Closing -= OnClosing;
        _allowClose = true;
        Close();
    }
}
