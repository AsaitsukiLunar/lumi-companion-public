/*
Traceability metadata
Version: 0.3.2-preview
Generated: 2026-07-25 (Asia/Shanghai)
Last updated: 2026-10-30
Content coverage through: 2026-10-30
Source type: LOCAL-WORK
Source: Codex Desktop / workspace new-chat / current Lumi independent companion task / Codex
*/

using System.Drawing;
using System.Windows.Media.Imaging;
using LumiCompanion.Infrastructure;
using LumiCompanion.Models;
using LumiCompanion.Native;
using Forms = System.Windows.Forms;

namespace LumiCompanion.UI;

public sealed class TrayController : IDisposable
{
    private readonly Forms.NotifyIcon _notifyIcon;
    private readonly Forms.ContextMenuStrip _menu;
    private readonly Forms.ToolStripMenuItem _pauseItem;
    private readonly Forms.ToolStripMenuItem _clickThroughItem;
    private readonly Forms.ToolStripMenuItem _startupItem;
    private readonly Forms.ToolStripMenuItem _usageVisibleItem;
    private readonly Forms.ToolStripMenuItem _usageRefreshItem;
    private readonly Forms.ToolStripMenuItem _usageStatusItem;
    private Icon? _icon;
    private bool _showCodexUsageResetTimes;
    private CodexUsageSnapshot? _lastUsageSnapshot;

    public TrayController(BitmapSource iconFrame, LumiSettings settings)
    {
        _icon = CreateIcon(iconFrame);
        _menu = new Forms.ContextMenuStrip();
        _menu.Items.Add(new Forms.ToolStripMenuItem("Lumi 独立桌宠 v" + AppIdentity.Version) { Enabled = false });
        _menu.Items.Add(new Forms.ToolStripSeparator());
        _menu.Items.Add(CreateItem("显示 / 隐藏", (_, _) => ToggleVisibilityRequested?.Invoke(this, EventArgs.Empty)));
        _menu.Items.Add(CreateItem("互动：招手", (_, _) => InteractionRequested?.Invoke(this, EventArgs.Empty)));
        _menu.Items.Add(CreateItem("设置…", (_, _) => SettingsRequested?.Invoke(this, EventArgs.Empty)));
        _menu.Items.Add(CreateItem("对话完成统计…", (_, _) => CompletionHistoryRequested?.Invoke(this, EventArgs.Empty)));
        _menu.Items.Add(CreateItem("ComfyUI 运行状态…", (_, _) => ComfyUiStatusRequested?.Invoke(this, EventArgs.Empty)));
        _menu.Items.Add(new Forms.ToolStripSeparator());

        _pauseItem = CreateItem("暂停事件感知", (_, _) => PauseToggled?.Invoke(this, EventArgs.Empty));
        _clickThroughItem = CreateItem("鼠标穿透（锁定）", (_, _) => ClickThroughToggled?.Invoke(this, EventArgs.Empty));
        _startupItem = CreateItem("开机启动", (_, _) => StartupToggled?.Invoke(this, EventArgs.Empty));
        _startupItem.Enabled = StartupManager.IsSupported;
        _menu.Items.Add(_pauseItem);
        _menu.Items.Add(_clickThroughItem);
        _menu.Items.Add(_startupItem);
        _menu.Items.Add(new Forms.ToolStripSeparator());

        _usageVisibleItem = CreateItem("显示 Codex 用量卡片", (_, _) => CodexUsageVisibilityToggled?.Invoke(this, EventArgs.Empty));
        _usageRefreshItem = CreateItem("刷新 Codex 用量", (_, _) => CodexUsageRefreshRequested?.Invoke(this, EventArgs.Empty));
        _usageStatusItem = new Forms.ToolStripMenuItem("Codex 用量：未启用") { Enabled = false };
        _menu.Items.Add(_usageVisibleItem);
        _menu.Items.Add(_usageRefreshItem);
        _menu.Items.Add(_usageStatusItem);
        _menu.Items.Add(new Forms.ToolStripSeparator());

        var offlineItem = new Forms.ToolStripMenuItem("智能 / 语音模块：未加载（离线）")
        {
            Enabled = false
        };
        _menu.Items.Add(offlineItem);
        _menu.Items.Add(new Forms.ToolStripSeparator());
        _menu.Items.Add(CreateItem("退出 Lumi", (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty)));

        _notifyIcon = new Forms.NotifyIcon
        {
            Icon = _icon,
            Text = "Lumi 独立桌宠 · 离线",
            ContextMenuStrip = _menu,
            Visible = true
        };
        _notifyIcon.DoubleClick += (_, _) => ToggleVisibilityRequested?.Invoke(this, EventArgs.Empty);
        UpdateSettings(settings);
    }

    public event EventHandler? ToggleVisibilityRequested;
    public event EventHandler? InteractionRequested;
    public event EventHandler? SettingsRequested;
    public event EventHandler? CompletionHistoryRequested;
    public event EventHandler? ComfyUiStatusRequested;
    public event EventHandler? PauseToggled;
    public event EventHandler? ClickThroughToggled;
    public event EventHandler? StartupToggled;
    public event EventHandler? CodexUsageVisibilityToggled;
    public event EventHandler? CodexUsageRefreshRequested;
    public event EventHandler? ExitRequested;

    public void UpdateSettings(LumiSettings settings)
    {
        _showCodexUsageResetTimes = settings.ShowCodexUsageResetTimes;
        _pauseItem.Checked = settings.SensorsPaused;
        _clickThroughItem.Checked = settings.ClickThrough;
        _startupItem.Checked = StartupManager.IsSupported && settings.StartWithWindows;
        _usageVisibleItem.Checked = settings.ShowCodexUsage;
        _usageRefreshItem.Enabled = settings.ShowCodexUsage;
        if (!settings.ShowCodexUsage)
        {
            _usageStatusItem.Text = "Codex 用量：未启用";
            _usageStatusItem.ToolTipText = "";
        }
        else if (_lastUsageSnapshot is { } snapshot) UpdateCodexUsage(snapshot);
        _notifyIcon.Text = settings.SensorsPaused
            ? "Lumi 独立桌宠 · 感知已暂停"
            : "Lumi 独立桌宠 · 离线感知中";
    }

    public void UpdateCodexUsage(CodexUsageSnapshot snapshot)
    {
        _lastUsageSnapshot = snapshot;
        UsageBadgeProjection display = UsageBadgeProjection.At(snapshot, DateTimeOffset.Now);
        if (!display.HasAnyQuota)
        {
            _usageStatusItem.Text = "Codex 用量：暂不可用";
            _usageStatusItem.ToolTipText = display.Status;
            return;
        }

        string fiveHour = display.FiveHour is null ? "未知" : display.FiveHour.RemainingPercent + "%";
        string weekly = display.Weekly is null ? "未知" : display.Weekly.RemainingPercent + "%";
        _usageStatusItem.Text = $"剩余额度：5小时 {fiveHour} · 本周 {weekly}" + (display.IsStale ? "（旧快照）" : "");
        _usageStatusItem.ToolTipText = (snapshot with
        {
            FiveHour = display.FiveHour, Weekly = display.Weekly,
            IsStale = display.IsStale, StatusMessage = display.Status
        }).BuildToolTip(_showCodexUsageResetTimes);
    }

    public void ShowMenu()
    {
        _menu.Show(Forms.Cursor.Position);
    }

    private static Forms.ToolStripMenuItem CreateItem(string text, EventHandler handler)
    {
        var item = new Forms.ToolStripMenuItem(text);
        item.Click += handler;
        return item;
    }

    private static Icon CreateIcon(BitmapSource source)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        stream.Position = 0;
        using var original = new Bitmap(stream);
        using var resized = new Bitmap(original, new Size(32, 32));
        IntPtr handle = resized.GetHicon();
        try
        {
            using Icon temporary = Icon.FromHandle(handle);
            return (Icon)temporary.Clone();
        }
        finally
        {
            NativeMethods.DestroyIcon(handle);
        }
    }

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _menu.Dispose();
        _icon?.Dispose();
        _icon = null;
    }
}
