// Version: 0.3.19-preview; Generated: 2026-10-30; Updated/Coverage: 2026-11-16 (Asia/Shanghai)
// Source: RAW | user compact usage and panel sizing requests; LOCAL-WORK | Codex Desktop / Windows.
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using LumiCompanion.Native;
using Controls = System.Windows.Controls;
using MediaColor = System.Windows.Media.Color;

namespace LumiCompanion.UI;

internal sealed class UsageDockButtonWindow : Window, IDisposable
{
    private const double DefaultWidth = 144;
    private const double DefaultHeight = 38;
    private readonly ScaleTransform _layoutScale = new(1, 1);
    private readonly Controls.Button _button;
    private readonly Controls.TextBlock _provider;
    private readonly Controls.TextBlock _summary;
    private bool _allowClose;
    internal event Action? ToggleRequested;
    internal event Action? HideRequested;

    internal UsageDockButtonWindow(CompanionWindow companion)
    {
        Owner = companion;
        Title = "Lumi · 紧凑用量";
        Width = DefaultWidth; Height = DefaultHeight;
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true; Background = Brushes.Transparent;
        ShowActivated = false; ShowInTaskbar = false; Topmost = companion.Topmost;
        var grid = new Controls.Grid { Margin = new Thickness(6, 3, 5, 3) };
        grid.ColumnDefinitions.Add(new Controls.ColumnDefinition());
        grid.ColumnDefinitions.Add(new Controls.ColumnDefinition { Width = new GridLength(22) });
        grid.ColumnDefinitions.Add(new Controls.ColumnDefinition { Width = new GridLength(22) });
        var text = new Controls.StackPanel();
        _provider = new Controls.TextBlock { Text = "1 · Codex", FontSize = 10, Foreground = Brushes.White, TextTrimming = TextTrimming.CharacterEllipsis };
        _summary = new Controls.TextBlock { Text = "用量未知", FontSize = 9, Foreground = UsageControlStyle.Muted, TextTrimming = TextTrimming.CharacterEllipsis };
        text.Children.Add(_provider); text.Children.Add(_summary);
        _button = new Controls.Button
        {
            Content = text, Style = UsageControlStyle.Button, Height = 30,
            Padding = new Thickness(3, 0, 1, 0),
            HorizontalContentAlignment = HorizontalAlignment.Left, ToolTip = "放大用量卡片"
        };
        _button.Click += (_, _) => ToggleRequested?.Invoke();
        grid.Children.Add(_button);
        var expand = new Controls.Button { Content = "+", Style = UsageControlStyle.Button, ToolTip = "放大用量卡片", VerticalAlignment = VerticalAlignment.Center };
        expand.Click += (_, _) => ToggleRequested?.Invoke();
        Controls.Grid.SetColumn(expand, 1); grid.Children.Add(expand);
        var close = new Controls.Button
        {
            Content = "×", Style = UsageControlStyle.Button,
            ToolTip = "隐藏用量卡；从托盘恢复", VerticalAlignment = VerticalAlignment.Center
        };
        close.Click += (_, _) => HideRequested?.Invoke();
        Controls.Grid.SetColumn(close, 2); grid.Children.Add(close);
        Content = new Controls.Border { Child = grid, CornerRadius = new CornerRadius(16), LayoutTransform = _layoutScale,
            Background = UsageControlStyle.Surface, BorderBrush = UsageControlStyle.Edge, BorderThickness = new Thickness(1) };
        SourceInitialized += (_, _) =>
        {
            IntPtr handle = new WindowInteropHelper(this).Handle;
            NativeMethods.SetClickThrough(handle, false);
            long style = NativeMethods.GetWindowLongPtr(handle, NativeMethods.GWL_EXSTYLE).ToInt64();
            NativeMethods.SetWindowLongPtr(handle, NativeMethods.GWL_EXSTYLE, new IntPtr(style | 0x08000000L | 0x80L));
        };
        Closing += (_, e) => { if (!_allowClose) { e.Cancel = true; HideRequested?.Invoke(); } };
    }

    internal void SetExpanded(bool expanded)
    {
        _button.ToolTip = "放大用量卡片";
    }

    internal void SetLayoutScale(double scale)
    {
        _layoutScale.ScaleX = _layoutScale.ScaleY = scale;
        Width = DefaultWidth * scale;
        Height = DefaultHeight * scale;
    }

    internal void SetSummary(string provider, string summary, object? detail)
    {
        _provider.Text = provider; _summary.Text = summary;
        _button.ToolTip = detail ?? "展开用量标签";
    }

    public void Dispose() { _allowClose = true; Close(); }
}
