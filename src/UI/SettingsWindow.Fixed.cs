/*
Traceability metadata
Version: 0.2.0
Generated: 2026-07-25 (Asia/Shanghai)
Last updated: 2026-11-10
Content coverage through: 2026-11-10
Source type: LOCAL-WORK; RAW (user quota reset time display request)
Source: Codex Desktop / workspace new-chat / current Lumi independent companion task / Codex
*/

using LumiCompanion.Infrastructure;
using LumiCompanion.Models;
using Wpf = System.Windows;
using Controls = System.Windows.Controls;
using Media = System.Windows.Media;

namespace LumiCompanion.UI;

public sealed class SettingsWindow : Wpf.Window
{
    private readonly Controls.Slider _width;
    private readonly Controls.TextBlock _widthValue;
    private readonly Controls.Slider _retention;
    private readonly Controls.TextBlock _retentionValue;
    private readonly Dictionary<string, Controls.CheckBox> _checks = new();

    public SettingsWindow(LumiSettings settings, string dataDirectory)
    {
        ResultSettings = settings.Clone();
        Title = "Lumi 设置";
        Width = 580;
        Height = 760;
        MinWidth = 500;
        MinHeight = 620;
        ShowInTaskbar = false;
        WindowStartupLocation = Wpf.WindowStartupLocation.CenterScreen;
        Background = new Media.SolidColorBrush(Media.Color.FromRgb(247, 248, 252));

        var root = new Controls.DockPanel();
        root.Children.Add(BuildFooter());

        var content = new Controls.StackPanel { Margin = new Wpf.Thickness(20) };
        content.Children.Add(new Controls.TextBlock
        {
            Text = "Lumi 独立桌宠设置",
            FontSize = 22,
            FontWeight = Wpf.FontWeights.SemiBold
        });
        content.Children.Add(new Controls.TextBlock
        {
            Text = "默认离线，只读取已授权的本机事件，不记录按键内容，也不主动控制电脑。",
            Margin = new Wpf.Thickness(0, 5, 0, 16),
            Foreground = Media.Brushes.DimGray,
            TextWrapping = Wpf.TextWrapping.Wrap
        });

        _width = CreateSlider(96, 320, settings.PetWidth, 1);
        _widthValue = ValueLabel();
        _width.ValueChanged += (_, _) => _widthValue.Text = Math.Round(_width.Value) + " px";
        var startup = Check("startWithWindows", "随 Windows 启动",
            StartupManager.IsSupported && settings.StartWithWindows,
            StartupManager.IsSupported ? "仅使用当前用户启动项。" : "运行文件夹版不设置 Windows 启动项。");
        startup.IsEnabled = StartupManager.IsSupported;
        content.Children.Add(Section("外观与系统",
            LabeledSlider("宠物宽度（96–320 px）", _width, _widthValue),
            Check("alwaysOnTop", "始终置顶", settings.AlwaysOnTop, "让 Lumi 保持在普通窗口上方。"),
            Check("clickThrough", "鼠标穿透（锁定）", settings.ClickThrough, "开启后通过托盘小图标重新配置。"),
            startup,
            Check("hideInFullscreen", "全屏时隐藏", settings.HideInFullscreen, "检测到全屏应用时暂时隐藏。")));

        content.Children.Add(Section("隐私与事件感知",
            Check("sensorsPaused", "暂停所有事件感知", settings.SensorsPaused, "动画仍可运行。"),
            Check("monitorInput", "检测键鼠活动", settings.MonitorInputActivity, "不记录字符、键码或扫描码。"),
            Check("distinguishInput", "区分键盘与鼠标活动", settings.DistinguishKeyboardAndMouse, "只保留活动类型和聚合计数。"),
            Check("monitorWindows", "检测窗口打开与切换", settings.MonitorWindows, "只检查可见顶层窗口。"),
            Check("readTitles", "读取窗口标题", settings.ReadWindowTitles, "标题可在内存中参与本地反应规则。"),
            Check("monitorAudio", "检测声音播放", settings.MonitorAudio, "只读取输出端点活跃度，不录制声音。"),
            Check("mouseLook", "视线跟随鼠标", settings.MouseLook, "只即时计算方向，不保存鼠标轨迹。")));

        _retention = CreateSlider(1, 90, settings.HistoryRetentionDays, 1);
        _retentionValue = ValueLabel();
        _retention.ValueChanged += (_, _) => _retentionValue.Text = Math.Round(_retention.Value) + " 天";

        var directory = new Controls.TextBox
        {
            Text = dataDirectory,
            IsReadOnly = true,
            Margin = new Wpf.Thickness(2, 6, 2, 8),
            Padding = new Wpf.Thickness(7),
            ToolTip = dataDirectory
        };
        var dataButtons = new Controls.StackPanel { Orientation = Controls.Orientation.Horizontal };
        Controls.Button open = SecondaryButton("打开数据目录");
        open.Click += (_, _) => OpenDataDirectoryRequested?.Invoke(this, EventArgs.Empty);
        Controls.Button clear = SecondaryButton("清除本地记录");
        clear.Margin = new Wpf.Thickness(8, 0, 0, 0);
        clear.Click += (_, _) =>
        {
            if (Wpf.MessageBox.Show(this, "确定清除状态、统计、历史和日志吗？设置会保留。", "Lumi", Wpf.MessageBoxButton.YesNo, Wpf.MessageBoxImage.Warning) == Wpf.MessageBoxResult.Yes)
            {
                ClearDataRequested?.Invoke(this, EventArgs.Empty);
            }
        };
        dataButtons.Children.Add(open);
        dataButtons.Children.Add(clear);

        content.Children.Add(Section("本地数据",
            Check("saveStats", "保存陪伴状态与聚合统计", settings.SaveLocalStats, "数据只存放在本机。"),
            Check("saveHistory", "保存分类后的事件历史", settings.SaveEventHistory, "默认关闭，不保存键鼠时间线。"),
            Check("storeTitles", "在事件历史中保存窗口标题", settings.StoreWindowTitles, "标题可能含文档名或网页名。"),
            LabeledSlider("历史保留天数", _retention, _retentionValue),
            new Controls.TextBlock { Text = "数据目录", FontWeight = Wpf.FontWeights.SemiBold, Margin = new Wpf.Thickness(2, 6, 2, 0) },
            directory,
            dataButtons));

        content.Children.Add(Section("可选模块",
            Check("showCodexUsage", "显示 Codex 用量卡片", settings.ShowCodexUsage, "读取本机 Codex 已有用量记录并显示采样时间；检查不会主动刷新服务器额度。"),
            Check("showCodexUsageResetTimes", "在 Codex 用量卡上显示 5 小时和周额度重置时间",
                settings.ShowCodexUsageResetTimes, "展开用量卡后显示两个额度窗口的本地重置时间。"),
            DisabledCheck("AI：未加载（离线）"),
            DisabledCheck("语音：未加载（离线）"),
            new Controls.TextBlock
            {
                Text = "用量卡片默认关闭；启用后只通过本机 Codex 读取额度，不保存账号或令牌。右键托盘小图标可以随时开关。",
                Foreground = Media.Brushes.DimGray,
                TextWrapping = Wpf.TextWrapping.Wrap,
                Margin = new Wpf.Thickness(2, 4, 2, 2)
            }));

        var scroll = new Controls.ScrollViewer
        {
            Content = content,
            VerticalScrollBarVisibility = Controls.ScrollBarVisibility.Auto
        };
        root.Children.Add(scroll);
        Content = root;

        _checks["monitorInput"].Checked += (_, _) => UpdateDependencies();
        _checks["monitorInput"].Unchecked += (_, _) => UpdateDependencies();
        _checks["monitorWindows"].Checked += (_, _) => UpdateDependencies();
        _checks["monitorWindows"].Unchecked += (_, _) => UpdateDependencies();
        _checks["readTitles"].Checked += (_, _) => UpdateDependencies();
        _checks["readTitles"].Unchecked += (_, _) => UpdateDependencies();
        _checks["saveHistory"].Checked += (_, _) => UpdateDependencies();
        _checks["saveHistory"].Unchecked += (_, _) => UpdateDependencies();
        _widthValue.Text = Math.Round(_width.Value) + " px";
        _retentionValue.Text = Math.Round(_retention.Value) + " 天";
        UpdateDependencies();
    }

    public LumiSettings ResultSettings { get; private set; }
    public event EventHandler? ClearDataRequested;
    public event EventHandler? OpenDataDirectoryRequested;

    private Wpf.UIElement BuildFooter()
    {
        var panel = new Controls.StackPanel
        {
            Orientation = Controls.Orientation.Horizontal,
            HorizontalAlignment = Wpf.HorizontalAlignment.Right
        };
        Controls.Button cancel = SecondaryButton("取消");
        cancel.IsCancel = true;
        cancel.Click += (_, _) => { DialogResult = false; };
        Controls.Button save = SecondaryButton("保存");
        save.IsDefault = true;
        save.Margin = new Wpf.Thickness(8, 0, 0, 0);
        save.Background = new Media.SolidColorBrush(Media.Color.FromRgb(91, 112, 238));
        save.Foreground = Media.Brushes.White;
        save.Click += (_, _) => SaveAndClose();
        panel.Children.Add(cancel);
        panel.Children.Add(save);

        var border = new Controls.Border
        {
            Child = panel,
            Background = Media.Brushes.White,
            BorderBrush = Media.Brushes.LightGray,
            BorderThickness = new Wpf.Thickness(0, 1, 0, 0),
            Padding = new Wpf.Thickness(18, 12, 18, 12)
        };
        Controls.DockPanel.SetDock(border, Controls.Dock.Bottom);
        return border;
    }

    private void SaveAndClose()
    {
        LumiSettings value = ResultSettings.Clone();
        value.PetWidth = (int)Math.Round(_width.Value);
        value.AlwaysOnTop = IsChecked("alwaysOnTop");
        value.ClickThrough = IsChecked("clickThrough");
        value.StartWithWindows = StartupManager.IsSupported && IsChecked("startWithWindows");
        value.HideInFullscreen = IsChecked("hideInFullscreen");
        value.SensorsPaused = IsChecked("sensorsPaused");
        value.MonitorInputActivity = IsChecked("monitorInput");
        value.DistinguishKeyboardAndMouse = value.MonitorInputActivity && IsChecked("distinguishInput");
        value.MonitorWindows = IsChecked("monitorWindows");
        value.ReadWindowTitles = value.MonitorWindows && IsChecked("readTitles");
        value.MonitorAudio = IsChecked("monitorAudio");
        value.MouseLook = IsChecked("mouseLook");
        value.ShowCodexUsage = IsChecked("showCodexUsage");
        value.ShowCodexUsageResetTimes = IsChecked("showCodexUsageResetTimes");
        value.SaveLocalStats = IsChecked("saveStats");
        value.SaveEventHistory = IsChecked("saveHistory");
        value.StoreWindowTitles = value.SaveEventHistory && value.ReadWindowTitles && IsChecked("storeTitles");
        value.HistoryRetentionDays = (int)Math.Round(_retention.Value);
        value.AiEnabled = false;
        value.VoiceEnabled = false;
        value.Normalize();
        ResultSettings = value;
        DialogResult = true;
    }

    private void UpdateDependencies()
    {
        _checks["distinguishInput"].IsEnabled = IsChecked("monitorInput");
        _checks["readTitles"].IsEnabled = IsChecked("monitorWindows");
        bool history = IsChecked("saveHistory");
        _checks["storeTitles"].IsEnabled = history && IsChecked("monitorWindows") && IsChecked("readTitles");
        _retention.IsEnabled = history;
        _retentionValue.IsEnabled = history;
        if (!history)
        {
            _checks["storeTitles"].IsChecked = false;
        }
    }

    private bool IsChecked(string key) => _checks[key].IsChecked == true;

    private Controls.CheckBox Check(string key, string label, bool value, string tip)
    {
        var check = new Controls.CheckBox
        {
            Content = label,
            IsChecked = value,
            ToolTip = tip,
            Margin = new Wpf.Thickness(2, 5, 2, 5)
        };
        _checks[key] = check;
        return check;
    }

    private static Controls.CheckBox DisabledCheck(string text) => new()
    {
        Content = text,
        IsChecked = false,
        IsEnabled = false,
        Margin = new Wpf.Thickness(2, 5, 2, 5)
    };

    private static Controls.GroupBox Section(string title, params Wpf.UIElement[] children)
    {
        var panel = new Controls.StackPanel();
        foreach (Wpf.UIElement child in children)
        {
            panel.Children.Add(child);
        }
        return new Controls.GroupBox
        {
            Header = title,
            Content = panel,
            Background = Media.Brushes.White,
            BorderBrush = Media.Brushes.LightGray,
            Margin = new Wpf.Thickness(0, 0, 0, 13),
            Padding = new Wpf.Thickness(12, 8, 12, 10)
        };
    }

    private static Controls.Grid LabeledSlider(string label, Controls.Slider slider, Controls.TextBlock value)
    {
        var grid = new Controls.Grid { Margin = new Wpf.Thickness(2, 4, 2, 8) };
        grid.ColumnDefinitions.Add(new Controls.ColumnDefinition());
        grid.ColumnDefinitions.Add(new Controls.ColumnDefinition { Width = Wpf.GridLength.Auto });
        grid.RowDefinitions.Add(new Controls.RowDefinition { Height = Wpf.GridLength.Auto });
        grid.RowDefinitions.Add(new Controls.RowDefinition { Height = Wpf.GridLength.Auto });
        var heading = new Controls.TextBlock { Text = label, FontWeight = Wpf.FontWeights.SemiBold };
        Controls.Grid.SetColumnSpan(heading, 2);
        grid.Children.Add(heading);
        Controls.Grid.SetRow(slider, 1);
        grid.Children.Add(slider);
        Controls.Grid.SetRow(value, 1);
        Controls.Grid.SetColumn(value, 1);
        grid.Children.Add(value);
        return grid;
    }

    private static Controls.Slider CreateSlider(double min, double max, double value, double tick) => new()
    {
        Minimum = min,
        Maximum = max,
        Value = Math.Clamp(value, min, max),
        TickFrequency = tick,
        IsSnapToTickEnabled = true,
        Margin = new Wpf.Thickness(0, 7, 12, 2)
    };

    private static Controls.TextBlock ValueLabel() => new()
    {
        MinWidth = 62,
        TextAlignment = Wpf.TextAlignment.Right,
        VerticalAlignment = Wpf.VerticalAlignment.Center,
        FontWeight = Wpf.FontWeights.SemiBold
    };

    private static Controls.Button SecondaryButton(string text) => new()
    {
        Content = text,
        MinWidth = 88,
        Padding = new Wpf.Thickness(14, 7, 14, 7),
        Background = Media.Brushes.White,
        BorderBrush = Media.Brushes.DarkGray
    };
}
