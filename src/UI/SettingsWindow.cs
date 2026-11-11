/*
Traceability metadata
Version: 0.1.0
Generated: 2026-07-25 (Asia/Shanghai)
Last updated: 2026-07-25
Content coverage through: 2026-07-25
Source type: LOCAL-WORK
Source: Codex Desktop / workspace new-chat / current Lumi independent companion task / Codex
*/

using LumiCompanion.Models;
using Wpf = System.Windows;
using Controls = System.Windows.Controls;
using Media = System.Windows.Media;

namespace LumiCompanion.UI;

public sealed class SettingsWindow : Wpf.Window
{
    private readonly string _dataDirectory;

    private readonly Controls.Slider _petWidthSlider;
    private readonly Controls.TextBlock _petWidthValue;
    private readonly Controls.CheckBox _alwaysOnTop;
    private readonly Controls.CheckBox _clickThrough;
    private readonly Controls.CheckBox _startWithWindows;
    private readonly Controls.CheckBox _sensorsPaused;
    private readonly Controls.CheckBox _monitorInput;
    private readonly Controls.CheckBox _distinguishKeyboardAndMouse;
    private readonly Controls.CheckBox _monitorWindows;
    private readonly Controls.CheckBox _readWindowTitles;
    private readonly Controls.CheckBox _monitorAudio;
    private readonly Controls.CheckBox _mouseLook;
    private readonly Controls.CheckBox _hideInFullscreen;
    private readonly Controls.CheckBox _saveLocalStats;
    private readonly Controls.CheckBox _saveEventHistory;
    private readonly Controls.CheckBox _storeWindowTitles;
    private readonly Controls.Slider _historyRetentionSlider;
    private readonly Controls.TextBlock _historyRetentionValue;

    public SettingsWindow(LumiSettings settings, string dataDirectory)
    {
        ArgumentNullException.ThrowIfNull(settings);

        _dataDirectory = dataDirectory ?? string.Empty;
        ResultSettings = CloneForEditing(settings);

        Title = "Lumi 设置";
        Width = 570;
        Height = 760;
        MinWidth = 500;
        MinHeight = 620;
        WindowStartupLocation = Wpf.WindowStartupLocation.CenterOwner;
        ResizeMode = Wpf.ResizeMode.CanResizeWithGrip;
        ShowInTaskbar = false;
        Background = new Media.SolidColorBrush(Media.Color.FromRgb(247, 248, 252));
        Foreground = new Media.SolidColorBrush(Media.Color.FromRgb(35, 39, 47));

        _petWidthSlider = CreateSlider(96, 320, ResultSettings.PetWidth, 1, 8);
        _petWidthValue = CreateValueLabel();
        _petWidthSlider.ValueChanged += (_, _) => UpdateWidthValue();

        _alwaysOnTop = CreateCheckBox("始终置顶", ResultSettings.AlwaysOnTop,
            "让 Lumi 保持在其他普通窗口上方。", "外观");
        _clickThrough = CreateCheckBox("点击穿透", ResultSettings.ClickThrough,
            "鼠标点击会穿过宠物；仍可通过托盘小图标重新打开设置。", "外观");
        _startWithWindows = CreateCheckBox("随 Windows 启动", ResultSettings.StartWithWindows,
            "仅使用当前用户的启动项，不需要管理员权限。", "系统");

        _sensorsPaused = CreateCheckBox("暂停感知", ResultSettings.SensorsPaused,
            "暂停键鼠、窗口和声音感知，但保留下面的配置。", "隐私");
        _monitorInput = CreateCheckBox("检测键鼠活动", ResultSettings.MonitorInputActivity,
            "仅判断是否有活动，不记录按键内容。", "感知");
        _distinguishKeyboardAndMouse = CreateCheckBox(
            "区分键盘与鼠标活动",
            ResultSettings.DistinguishKeyboardAndMouse,
            "只产生键盘/鼠标活动类型，不保存键码、字符或鼠标轨迹。",
            "感知");
        _monitorWindows = CreateCheckBox("检测窗口切换与打开", ResultSettings.MonitorWindows,
            "用于让 Lumi 对应用切换和新窗口做出反应。", "感知");
        _readWindowTitles = CreateCheckBox("读取窗口标题", ResultSettings.ReadWindowTitles,
            "标题可在本机参与反应规则；是否写入磁盘由下方单独控制。", "隐私");
        _monitorAudio = CreateCheckBox("检测声音播放", ResultSettings.MonitorAudio,
            "只读取默认输出设备的活动和峰值，不录制声音。", "感知");
        _mouseLook = CreateCheckBox("观察鼠标位置", ResultSettings.MouseLook,
            "让 Lumi 的视线跟随鼠标；不保存鼠标轨迹。", "感知");
        _hideInFullscreen = CreateCheckBox("全屏时隐藏 Lumi", ResultSettings.HideInFullscreen,
            "检测到全屏应用时暂时隐藏宠物。", "外观");

        _saveLocalStats = CreateCheckBox("保存本地统计", ResultSettings.SaveLocalStats,
            "保存陪伴时长、互动次数等聚合数据，不记录输入内容。", "数据");
        _saveEventHistory = CreateCheckBox("保存事件历史", ResultSettings.SaveEventHistory,
            "在本机保存经过归类的事件记录，默认关闭。", "数据");
        _storeWindowTitles = CreateCheckBox("在事件历史中保存窗口标题", ResultSettings.StoreWindowTitles,
            "标题可能包含文档名或网页名；只在事件历史开启时可用。", "隐私");

        _historyRetentionSlider = CreateSlider(
            1,
            90,
            ResultSettings.HistoryRetentionDays,
            1,
            7);
        _historyRetentionValue = CreateValueLabel();
        _historyRetentionSlider.ValueChanged += (_, _) => UpdateRetentionValue();

        _monitorInput.Checked += (_, _) => UpdateDependencies();
        _monitorInput.Unchecked += (_, _) => UpdateDependencies();
        _monitorWindows.Checked += (_, _) => UpdateDependencies();
        _monitorWindows.Unchecked += (_, _) => UpdateDependencies();
        _readWindowTitles.Checked += (_, _) => UpdateDependencies();
        _readWindowTitles.Unchecked += (_, _) => UpdateDependencies();
        _saveEventHistory.Checked += (_, _) => UpdateDependencies();
        _saveEventHistory.Unchecked += (_, _) => UpdateDependencies();

        Content = BuildLayout();
        UpdateWidthValue();
        UpdateRetentionValue();
        UpdateDependencies();
    }

    public LumiSettings ResultSettings { get; private set; }

    public event EventHandler? ClearDataRequested;

    public event EventHandler? OpenDataDirectoryRequested;

    private Wpf.UIElement BuildLayout()
    {
        var root = new Controls.DockPanel();

        Controls.Border footer = BuildFooter();
        Controls.DockPanel.SetDock(footer, Controls.Dock.Bottom);
        root.Children.Add(footer);

        var heading = new Controls.StackPanel
        {
            Margin = new Wpf.Thickness(24, 20, 24, 12),
        };
        heading.Children.Add(new Controls.TextBlock
        {
            Text = "Lumi 独立桌宠设置",
            FontSize = 22,
            FontWeight = Wpf.FontWeights.SemiBold,
        });
        heading.Children.Add(new Controls.TextBlock
        {
            Text = "基础版默认离线，只感知本机事件，不主动控制电脑。",
            Margin = new Wpf.Thickness(0, 5, 0, 0),
            Foreground = new Media.SolidColorBrush(Media.Color.FromRgb(91, 98, 112)),
            TextWrapping = Wpf.TextWrapping.Wrap,
        });
        Controls.DockPanel.SetDock(heading, Controls.Dock.Top);
        root.Children.Add(heading);

        var sections = new Controls.StackPanel
        {
            Margin = new Wpf.Thickness(20, 0, 20, 20),
        };

        var sizePanel = new Controls.Grid();
        sizePanel.ColumnDefinitions.Add(new Controls.ColumnDefinition());
        sizePanel.ColumnDefinitions.Add(new Controls.ColumnDefinition
        {
            Width = Wpf.GridLength.Auto,
        });
        sizePanel.Children.Add(_petWidthSlider);
        Controls.Grid.SetColumn(_petWidthValue, 1);
        sizePanel.Children.Add(_petWidthValue);

        sections.Children.Add(CreateSection(
            "外观与系统",
            CreateLabeledControl("宠物宽度（96–320 px）", sizePanel),
            _alwaysOnTop,
            _clickThrough,
            _startWithWindows,
            _hideInFullscreen));

        sections.Children.Add(CreateSection(
            "隐私与感知",
            _sensorsPaused,
            _monitorInput,
            _distinguishKeyboardAndMouse,
            _monitorWindows,
            _readWindowTitles,
            _monitorAudio,
            _mouseLook));

        var retentionPanel = new Controls.Grid();
        retentionPanel.ColumnDefinitions.Add(new Controls.ColumnDefinition());
        retentionPanel.ColumnDefinitions.Add(new Controls.ColumnDefinition
        {
            Width = Wpf.GridLength.Auto,
        });
        retentionPanel.Children.Add(_historyRetentionSlider);
        Controls.Grid.SetColumn(_historyRetentionValue, 1);
        retentionPanel.Children.Add(_historyRetentionValue);

        var directoryText = new Controls.TextBox
        {
            Text = _dataDirectory,
            IsReadOnly = true,
            Margin = new Wpf.Thickness(0, 5, 0, 8),
            Padding = new Wpf.Thickness(8, 5, 8, 5),
            Background = new Media.SolidColorBrush(Media.Color.FromRgb(242, 244, 248)),
            BorderBrush = new Media.SolidColorBrush(Media.Color.FromRgb(205, 210, 220)),
            HorizontalScrollBarVisibility = Controls.ScrollBarVisibility.Auto,
            ToolTip = _dataDirectory,
        };

        var dataButtons = new Controls.StackPanel
        {
            Orientation = Controls.Orientation.Horizontal,
            Margin = new Wpf.Thickness(0, 2, 0, 0),
        };
        Controls.Button openDirectory = CreateSecondaryButton("打开数据目录");
        openDirectory.Click += (_, _) =>
            OpenDataDirectoryRequested?.Invoke(this, EventArgs.Empty);
        Controls.Button clearData = CreateSecondaryButton("清除本地数据");
        clearData.Margin = new Wpf.Thickness(8, 0, 0, 0);
        clearData.Click += OnClearDataClick;
        dataButtons.Children.Add(openDirectory);
        dataButtons.Children.Add(clearData);

        sections.Children.Add(CreateSection(
            "本地数据",
            _saveLocalStats,
            _saveEventHistory,
            _storeWindowTitles,
            CreateLabeledControl("事件历史保留天数（1–90 天）", retentionPanel),
            CreateLabeledControl("数据目录", directoryText),
            dataButtons));

        Controls.CheckBox aiStatus = CreateCheckBox(
            "AI：未加载，离线",
            false,
            "未来可通过托盘小图标重新配置可选 AI 模块。",
            "可选模块");
        aiStatus.IsEnabled = false;

        Controls.CheckBox voiceStatus = CreateCheckBox(
            "语音：未加载，离线",
            false,
            "未来可单独配置语音输出；基础版不会使用麦克风。",
            "可选模块");
        voiceStatus.IsEnabled = false;

        sections.Children.Add(CreateSection(
            "可选模块",
            aiStatus,
            voiceStatus,
            new Controls.TextBlock
            {
                Text = "当前版本不包含 AI 或语音提供方，也不会发起网络请求。",
                Margin = new Wpf.Thickness(2, 3, 2, 2),
                Foreground = new Media.SolidColorBrush(Media.Color.FromRgb(91, 98, 112)),
                TextWrapping = Wpf.TextWrapping.Wrap,
            }));

        var scroll = new Controls.ScrollViewer
        {
            Content = sections,
            VerticalScrollBarVisibility = Controls.ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = Controls.ScrollBarVisibility.Disabled,
        };
        root.Children.Add(scroll);

        return root;
    }

    private Controls.Border BuildFooter()
    {
        var buttons = new Controls.StackPanel
        {
            Orientation = Controls.Orientation.Horizontal,
            HorizontalAlignment = Wpf.HorizontalAlignment.Right,
        };

        Controls.Button cancel = CreateSecondaryButton("取消");
        cancel.IsCancel = true;
        cancel.Click += (_, _) => CloseDialog(false);

        var save = new Controls.Button
        {
            Content = "保存",
            IsDefault = true,
            MinWidth = 88,
            Padding = new Wpf.Thickness(16, 8, 16, 8),
            Margin = new Wpf.Thickness(10, 0, 0, 0),
            Background = new Media.SolidColorBrush(Media.Color.FromRgb(91, 112, 238)),
            Foreground = Media.Brushes.White,
            BorderThickness = new Wpf.Thickness(0),
        };
        save.Click += OnSaveClick;

        buttons.Children.Add(cancel);
        buttons.Children.Add(save);

        return new Controls.Border
        {
            Child = buttons,
            Padding = new Wpf.Thickness(20, 13, 20, 13),
            Background = Media.Brushes.White,
            BorderBrush = new Media.SolidColorBrush(Media.Color.FromRgb(221, 224, 232)),
            BorderThickness = new Wpf.Thickness(0, 1, 0, 0),
        };
    }

    private void OnSaveClick(object sender, Wpf.RoutedEventArgs e)
    {
        var result = CloneForEditing(ResultSettings);

        result.PetWidth = (int)Math.Round(_petWidthSlider.Value);
        result.AlwaysOnTop = _alwaysOnTop.IsChecked == true;
        result.ClickThrough = _clickThrough.IsChecked == true;
        result.StartWithWindows = _startWithWindows.IsChecked == true;
        result.SensorsPaused = _sensorsPaused.IsChecked == true;
        result.MonitorInputActivity = _monitorInput.IsChecked == true;
        result.DistinguishKeyboardAndMouse =
            result.MonitorInputActivity && _distinguishKeyboardAndMouse.IsChecked == true;
        result.MonitorWindows = _monitorWindows.IsChecked == true;
        result.ReadWindowTitles =
            result.MonitorWindows && _readWindowTitles.IsChecked == true;
        result.MonitorAudio = _monitorAudio.IsChecked == true;
        result.MouseLook = _mouseLook.IsChecked == true;
        result.HideInFullscreen = _hideInFullscreen.IsChecked == true;
        result.SaveLocalStats = _saveLocalStats.IsChecked == true;
        result.SaveEventHistory = _saveEventHistory.IsChecked == true;
        result.StoreWindowTitles =
            result.SaveEventHistory &&
            result.ReadWindowTitles &&
            _storeWindowTitles.IsChecked == true;
        result.HistoryRetentionDays = (int)Math.Round(_historyRetentionSlider.Value);
        result.AiEnabled = false;
        result.VoiceEnabled = false;

        result.Normalize();
        ResultSettings = result;
        CloseDialog(true);
    }

    private void OnClearDataClick(object sender, Wpf.RoutedEventArgs e)
    {
        Wpf.MessageBoxResult answer = Wpf.MessageBox.Show(
            this,
            "确定要清除 Lumi 的本地状态、统计和事件历史吗？设置本身会保留。",
            "清除本地数据",
            Wpf.MessageBoxButton.YesNo,
            Wpf.MessageBoxImage.Warning,
            Wpf.MessageBoxResult.No);

        if (answer == Wpf.MessageBoxResult.Yes)
        {
            ClearDataRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    private void CloseDialog(bool result)
    {
        try
        {
            DialogResult = result;
        }
        catch (InvalidOperationException)
        {
            // Also allow the host to show this window non-modally.
            Close();
        }
    }

    private void UpdateDependencies()
    {
        bool inputEnabled = _monitorInput.IsChecked == true;
        _distinguishKeyboardAndMouse.IsEnabled = inputEnabled;

        bool windowsEnabled = _monitorWindows.IsChecked == true;
        _readWindowTitles.IsEnabled = windowsEnabled;

        bool historyEnabled = _saveEventHistory.IsChecked == true;
        bool titleStorageEnabled =
            historyEnabled && windowsEnabled && _readWindowTitles.IsChecked == true;
        _storeWindowTitles.IsEnabled = titleStorageEnabled;
        _historyRetentionSlider.IsEnabled = historyEnabled;
        _historyRetentionValue.IsEnabled = historyEnabled;

        if (!historyEnabled)
        {
            _storeWindowTitles.IsChecked = false;
        }
    }

    private void UpdateWidthValue()
    {
        _petWidthValue.Text = $"{Math.Round(_petWidthSlider.Value):0} px";
    }

    private void UpdateRetentionValue()
    {
        _historyRetentionValue.Text = $"{Math.Round(_historyRetentionSlider.Value):0} 天";
    }

    private static LumiSettings CloneForEditing(LumiSettings source)
    {
        LumiSettings clone = source.Clone();
        TraceMetadata metadata = source.Metadata ?? new TraceMetadata();
        clone.Metadata = new TraceMetadata
        {
            Version = metadata.Version,
            GeneratedAt = metadata.GeneratedAt,
            LastUpdatedAt = metadata.LastUpdatedAt,
            ContentCoverageThrough = metadata.ContentCoverageThrough,
            SourceType = metadata.SourceType,
            Source = metadata.Source,
        };
        return clone;
    }

    private static Controls.CheckBox CreateCheckBox(
        string text,
        bool isChecked,
        string toolTip,
        string automationGroup)
    {
        return new Controls.CheckBox
        {
            Content = text,
            IsChecked = isChecked,
            Margin = new Wpf.Thickness(2, 5, 2, 5),
            Padding = new Wpf.Thickness(0, 2, 0, 2),
            ToolTip = toolTip,
            Tag = automationGroup,
        };
    }

    private static Controls.Slider CreateSlider(
        double minimum,
        double maximum,
        double value,
        double smallChange,
        double largeChange)
    {
        return new Controls.Slider
        {
            Minimum = minimum,
            Maximum = maximum,
            Value = Math.Clamp(value, minimum, maximum),
            SmallChange = smallChange,
            LargeChange = largeChange,
            TickFrequency = smallChange,
            IsSnapToTickEnabled = true,
            VerticalAlignment = Wpf.VerticalAlignment.Center,
            Margin = new Wpf.Thickness(0, 4, 12, 4),
        };
    }

    private static Controls.TextBlock CreateValueLabel()
    {
        return new Controls.TextBlock
        {
            MinWidth = 60,
            TextAlignment = Wpf.TextAlignment.Right,
            VerticalAlignment = Wpf.VerticalAlignment.Center,
            FontWeight = Wpf.FontWeights.SemiBold,
        };
    }

    private static Controls.FrameworkElement CreateLabeledControl(
        string label,
        Wpf.UIElement control)
    {
        var panel = new Controls.StackPanel
        {
            Margin = new Wpf.Thickness(2, 5, 2, 7),
        };
        panel.Children.Add(new Controls.TextBlock
        {
            Text = label,
            Margin = new Wpf.Thickness(0, 0, 0, 3),
            FontWeight = Wpf.FontWeights.SemiBold,
        });
        panel.Children.Add(control);
        return panel;
    }

    private static Controls.GroupBox CreateSection(
        string header,
        params Wpf.UIElement[] children)
    {
        var panel = new Controls.StackPanel
        {
            Margin = new Wpf.Thickness(3, 1, 3, 4),
        };
        foreach (Wpf.UIElement child in children)
        {
            panel.Children.Add(child);
        }

        return new Controls.GroupBox
        {
            Header = header,
            Content = panel,
            Margin = new Wpf.Thickness(0, 0, 0, 13),
            Padding = new Wpf.Thickness(12, 8, 12, 10),
            Background = Media.Brushes.White,
            BorderBrush = new Media.SolidColorBrush(Media.Color.FromRgb(218, 222, 231)),
        };
    }

    private static Controls.Button CreateSecondaryButton(string text)
    {
        return new Controls.Button
        {
            Content = text,
            MinWidth = 88,
            Padding = new Wpf.Thickness(14, 7, 14, 7),
            Background = Media.Brushes.White,
            BorderBrush = new Media.SolidColorBrush(Media.Color.FromRgb(190, 196, 208)),
            BorderThickness = new Wpf.Thickness(1),
        };
    }
}
