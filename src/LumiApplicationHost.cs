/*
Traceability metadata
Version: 0.3.22-beta.1
Generated: 2026-07-25 (Asia/Shanghai)
Last updated / content coverage through: 2026-11-17 (Asia/Shanghai)
Source type: RAW (user feedback), LOCAL-WORK, LOCAL-AGENT
Original sources: Lumi host, WorkBuddy state bridge, and GitHub release checks
Organizing node: Windows / Codex Desktop / Lumi independent companion / Codex
*/

using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using LumiCompanion.Animation;
using LumiCompanion.Behavior;
using LumiCompanion.Infrastructure;
using LumiCompanion.Integration;
using LumiCompanion.Models;
using LumiCompanion.Sensors;
using LumiCompanion.UI;
using LumiCompanion.Windowing;

namespace LumiCompanion;

public sealed class LumiApplicationHost : IDisposable
{
    private readonly Application _application;
    private readonly StorageService _storage;
    private readonly AnimationCatalog _catalog;
    private readonly DispatcherTimer _behaviorTimer;
    private readonly DispatcherTimer _audioTimer;
    private readonly DispatcherTimer _persistenceTimer;
    private readonly DispatcherTimer _codexUsageTimer;
    private readonly DispatcherTimer _taskStatusTimer;
    private readonly DispatcherTimer _updateTimer;
    private readonly GitHubUpdateService _updates = new();
    private readonly CancellationTokenSource _updateCancellation = new();
    private SettingsWindow? _settingsDialog;
    private GitHubUpdateStatus? _updateStatus;
    private DateTimeOffset? _updateCheckedAt;
    private bool _checkingUpdates;
    private readonly LocalCodexTaskSource _taskSource = new();
    private readonly CompletionHistoryService _completionHistory;
    private readonly LocalWorkBuddyTaskSource _workBuddyTaskSource = new();
    private readonly WorkBuddyCompletionHistory _workBuddyCompletionHistory;
    private CompletionHistoryWindow? _completionHistoryWindow;
    private ComfyUiStatusWindow? _comfyUiStatusWindow;
    private readonly CancellationTokenSource _taskStatusCancellation = new();
    private LocalTaskStatusCoordinator? _taskStatusCoordinator;
    private LocalTaskStatusCoordinator? _workBuddyTaskStatusCoordinator;
    private int _taskStatusReadActive;
    private readonly AudioPeakMonitor _audioMonitor;
    private readonly CodexUsageService _codexUsageService;
    private LumiSettings _settings;
    private LumiPersistentState _state;
    private BehaviorController? _behavior;
    private CompanionWindow? _window;
    private CodexUsageBadgeWindow? _codexUsageBadge;
    private TaskStatusBubbleWindow? _taskStatusBubble;
    private TrayController? _tray;
    private DesktopActivityMonitor? _activityMonitor;
    private CodexWindowTargetTracker? _codexWindowTargetTracker;
    private int _audioHighSamples;
    private int _audioLowSamples;
    private bool _audioActive;
    private int? _lastLookIndex;
    private bool _exiting;
    private bool _disposed;
    private bool _sessionLocked;
    private bool _systemSuspended;
    private bool _sessionDisconnected;
    private bool _sessionPaused;
    private CancellationTokenSource? _codexUsageRefreshCts;
    private int _codexUsageRefreshActive;

    public LumiApplicationHost(Application application)
    {
        _application = application;
        _storage = new StorageService();
        _settings = _storage.LoadSettings();
        _settings.StartWithWindows = StartupManager.IsEnabled();
        _state = _storage.LoadState();
        _completionHistory = new CompletionHistoryService(_storage.RootDirectory, _settings.SaveLocalStats);
        _workBuddyCompletionHistory = new WorkBuddyCompletionHistory(_settings.SaveLocalStats);
        _catalog = new AnimationCatalog();
        _audioMonitor = new AudioPeakMonitor();
        _codexUsageService = new CodexUsageService();

        _behaviorTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(33)
        };
        _behaviorTimer.Tick += OnBehaviorTick;

        _audioTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(200)
        };
        _audioTimer.Tick += OnAudioTick;

        _persistenceTimer = new DispatcherTimer(DispatcherPriority.ApplicationIdle)
        {
            Interval = TimeSpan.FromMinutes(1)
        };
        _persistenceTimer.Tick += OnPersistenceTick;

        _codexUsageTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMinutes(_settings.CodexUsageRefreshMinutes)
        };
        _codexUsageTimer.Tick += OnCodexUsageTimerTick;
        _taskStatusTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(2)
        };
        _taskStatusTimer.Tick += OnTaskStatusTimerTick;
        _updateTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromHours(12) };
        _updateTimer.Tick += OnUpdateTimerTick;
    }

    public void Start()
    {
        _behavior = new BehaviorController(_settings);
        _taskStatusCoordinator = new LocalTaskStatusCoordinator(_behavior);
        _workBuddyTaskStatusCoordinator = new LocalTaskStatusCoordinator(_behavior, source: LocalWorkBuddyTaskSource.SourceId);
        _behavior.VisualStateChanged += OnVisualStateChanged;

        _window = new CompanionWindow(_catalog, _settings);
        _window.AnimationCompleted += OnAnimationCompleted;
        _window.DraggingChanged += OnDraggingChanged;
        _window.DragMotionChanged += OnDragMotionChanged;
        _window.InteractionRequested += OnInteractionRequested;
        _window.Dragged += OnDragged;
        _window.ContextMenuRequested += OnContextMenuRequested;
        _window.PositionChangedByUser += OnPositionChangedByUser;
        _window.WidthChangedByUser += OnWidthChangedByUser;
        _window.ShowWithoutActivation();

        _codexUsageBadge = new CodexUsageBadgeWindow(_window);
        _codexUsageBadge.ModuleVisibilityRequested += OnUsageModuleVisibilityRequested;
        _codexUsageBadge.SetSnapshot(CodexUsageSnapshot.Loading());
        _codexUsageBadge.ApplySettings(_settings);
        _taskStatusBubble = new TaskStatusBubbleWindow(_window);
        _taskStatusBubble.ApplySettings(_settings);
        _codexUsageBadge.SetStatusBubble(_taskStatusBubble);
        _comfyUiStatusWindow = new ComfyUiStatusWindow(_window, _state.ComfyUiPanel, _codexUsageBadge, _taskStatusBubble);
        _comfyUiStatusWindow.ApplySettings(_settings);
        _comfyUiStatusWindow.PlacementChanged += OnComfyUiPanelPlacementChanged;
        _comfyUiStatusWindow.SyncVisibility();

        if (_window.HwndSource is null)
        {
            throw new InvalidOperationException("Unable to initialize the Lumi desktop window handle.");
        }

        _activityMonitor = new DesktopActivityMonitor(_window.HwndSource, _settings);
        _codexWindowTargetTracker = new CodexWindowTargetTracker();
        _activityMonitor.InputSampled += OnInputSampled;
        _activityMonitor.ForegroundChanged += OnForegroundChanged;
        _activityMonitor.WindowOpened += OnWindowOpened;
        _activityMonitor.FullscreenChanged += OnFullscreenChanged;
        _activityMonitor.SessionChanged += OnSessionChanged;

        _tray = new TrayController(_catalog.Get(AnimationId.Idle).Frames[0], _settings);
        _tray.ToggleVisibilityRequested += OnToggleVisibilityRequested;
        _tray.InteractionRequested += OnTrayInteractionRequested;
        _tray.SettingsRequested += OnSettingsRequested;
        _tray.CompletionHistoryRequested += OnCompletionHistoryRequested;
        _tray.ComfyUiStatusRequested += OnComfyUiStatusRequested;
        _tray.PauseToggled += OnPauseToggled;
        _tray.ClickThroughToggled += OnClickThroughToggled;
        _tray.StartupToggled += OnStartupToggled;
        _tray.CodexUsageVisibilityToggled += OnCodexUsageVisibilityToggled;
        _tray.CodexUsageRefreshRequested += OnCodexUsageRefreshRequested;
        _tray.ExitRequested += OnExitRequested;

        UpdateSensorLifecycle();
        _application.Exit += OnApplicationExit;
        _storage.PruneHistory(_settings.HistoryRetentionDays);
        _behaviorTimer.Start();
        _audioTimer.Start();
        _persistenceTimer.Start();
        _taskStatusTimer.Start();
        _updateTimer.Start();
        _ = CheckForUpdatesAsync();
        _ = RefreshTaskStatusAsync();
        ConfigureCodexUsageLifecycle(refreshImmediately: true);
        _behavior.HandleDirectInteraction(false);
    }

    private void OnContextMenuRequested(object? sender, EventArgs e)
    {
        _tray?.ShowMenu();
    }

    private void OnPositionChangedByUser(object? sender, EventArgs e)
    {
        SaveWindowPosition();
    }

    private void OnWidthChangedByUser(object? sender, int width)
    {
        _settings.PetWidth = width;
        _storage.SaveSettings(_settings);
    }

    private void OnFullscreenChanged(object? sender, bool fullscreen)
    {
        _window?.SetFullscreenHidden(fullscreen);
    }

    private void OnToggleVisibilityRequested(object? sender, EventArgs e)
    {
        _window?.ToggleVisible();
    }

    private void OnTrayInteractionRequested(object? sender, EventArgs e)
    {
        OnInteractionRequested(this, false);
    }

    private void OnSettingsRequested(object? sender, EventArgs e)
    {
        ShowSettings();
    }

    private void OnPauseToggled(object? sender, EventArgs e)
    {
        LumiSettings updated = _settings.Clone();
        updated.SensorsPaused = !updated.SensorsPaused;
        ApplySettings(updated, false);
    }

    private void OnClickThroughToggled(object? sender, EventArgs e)
    {
        LumiSettings updated = _settings.Clone();
        updated.ClickThrough = !updated.ClickThrough;
        ApplySettings(updated, false);
    }

    private void OnStartupToggled(object? sender, EventArgs e)
    {
        LumiSettings updated = _settings.Clone();
        updated.StartWithWindows = !updated.StartWithWindows;
        ApplySettings(updated, true);
    }

    private void OnCodexUsageVisibilityToggled(object? sender, EventArgs e)
    {
        LumiSettings updated = _settings.Clone();
        updated.ShowCodexUsage = !updated.ShowCodexUsage;
        ApplySettings(updated, false);
    }

    private void OnCodexUsageRefreshRequested(object? sender, EventArgs e)
    {
        _ = RefreshCodexUsageAsync();
    }

    private void OnUsageModuleVisibilityRequested(bool visible)
    {
        LumiSettings updated = _settings.Clone();
        updated.ShowCodexUsage = visible;
        ApplySettings(updated, false);
    }

    private void OnExitRequested(object? sender, EventArgs e)
    {
        Exit();
    }

    private void OnBehaviorTick(object? sender, EventArgs e)
    {
        if (_behavior is null || _window is null)
        {
            return;
        }

        _codexWindowTargetTracker?.Refresh();
        _behavior.SetBodyHovered(_window.IsBodyHovered);
        if (!_settings.SensorsPaused && _settings.MouseLook && _window.IsVisible &&
            DesktopActivityMonitor.TryGetCursorPosition(out Point cursor))
        {
            Point local = _window.PointFromScreen(cursor);
            // The idle frame's eye midpoint is the stable gaze origin for all pose frames.
            double dx = local.X - _window.ActualWidth * (110.0 / 192.0);
            double dy = local.Y - _window.ActualHeight * (59.0 / 208.0);
            double radius = Math.Max(_window.ActualWidth * 2.0, 280);
            double distanceSquared = dx * dx + dy * dy;
            if (distanceSquared >= 8 * 8 && distanceSquared <= radius * radius)
            {
                double degrees = (Math.Atan2(dx, -dy) * 180.0 / Math.PI + 360.0) % 360.0;
                int candidate = (int)Math.Round(degrees / 22.5, MidpointRounding.AwayFromZero) % 16;
                if (!_lastLookIndex.HasValue || ShouldChangeLookDirection(_lastLookIndex.Value, degrees))
                {
                    _lastLookIndex = candidate;
                }
                _behavior.SetLookDirection(_lastLookIndex);
            }
            else
            {
                _lastLookIndex = null;
                _behavior.SetLookDirection(null);
            }
        }
        else
        {
            _lastLookIndex = null;
            _behavior.SetLookDirection(null);
        }

        _behavior.Tick();
        _taskStatusBubble?.SetTaskState(_behavior.GetTaskState());
    }

    private void OnAnimationCompleted(AnimationId animation) => _behavior?.HandleAnimationCompleted(animation);

    private static bool ShouldChangeLookDirection(int currentIndex, double degrees)
    {
        double currentCenter = currentIndex * 22.5;
        double difference = Math.Abs(((degrees - currentCenter + 540.0) % 360.0) - 180.0);
        return difference > 16.25; // 11.25-degree cell half-width plus 5-degree hysteresis.
    }

    private void OnAudioTick(object? sender, EventArgs e)
    {
        if (_behavior is null || !ShouldMonitorAudio())
        {
            ResetAudioActivity();
            return;
        }

        float peak = _audioMonitor.GetPeakValue();
        if (peak >= 0.003f)
        {
            _audioHighSamples++;
            _audioLowSamples = 0;
        }
        else
        {
            _audioLowSamples++;
            _audioHighSamples = 0;
        }

        if (!_audioActive && _audioHighSamples >= 3)
        {
            _audioActive = true;
            _state.AudioStartCount++;
            _behavior.HandleAudioChanged(true);
            _storage.AppendHistory(_settings, new EventHistoryRecord
            {
                Type = "audio-started",
                Category = "audio",
                Detail = "Default output endpoint became active; no audio samples were captured."
            });
        }
        else if (_audioActive && _audioLowSamples >= 10)
        {
            _audioActive = false;
            _behavior.HandleAudioChanged(false);
            _storage.AppendHistory(_settings, new EventHistoryRecord
            {
                Type = "audio-stopped",
                Category = "audio",
                Detail = "Default output endpoint stayed below the activity threshold for two seconds."
            });
        }
    }

    private void OnPersistenceTick(object? sender, EventArgs e)
    {
        _state.CompanionMinutes++;
        if (!_settings.SensorsPaused && !_sessionPaused && _settings.MonitorInputActivity &&
            DesktopActivityMonitor.GetIdleSeconds() < 60)
        {
            _state.ActiveMinutes++;
        }

        if (_settings.SaveLocalStats)
        {
            _storage.SaveState(_state);
        }
        SaveWindowPosition();
    }

    private void OnCodexUsageTimerTick(object? sender, EventArgs e)
    {
        _ = RefreshCodexUsageAsync();
    }

    private void OnTaskStatusTimerTick(object? sender, EventArgs e) => _ = RefreshTaskStatusAsync();

    private void OnUpdateTimerTick(object? sender, EventArgs e) => _ = CheckForUpdatesAsync();

    private async Task CheckForUpdatesAsync()
    {
        if (_disposed || _checkingUpdates) return;
        _checkingUpdates = true;
        RefreshUpdateDisplay();
        CancellationToken token = _updateCancellation.Token;
        try
        {
            _updateStatus = await _updates.CheckAsync(AppIdentity.Version, token);
            _updateCheckedAt = DateTimeOffset.Now;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        finally
        {
            _checkingUpdates = false;
            if (!_disposed) RefreshUpdateDisplay();
        }
    }

    private void RefreshUpdateDisplay()
    {
        string text = _checkingUpdates ? "正在检查 GitHub 发布…" : _updateStatus switch
        {
            { Error: { } error } => error,
            { UpdateAvailable: true, LatestVersion: { } latest } => $"GitHub 有新版 v{latest}",
            { LatestVersion: { } latest } => $"GitHub 最新发布 v{latest}，当前无需更新",
            _ => "等待读取 GitHub 发布"
        };
        if (!_checkingUpdates && _updateCheckedAt is { } checkedAt) text += $" · {checkedAt:MM-dd HH:mm} 检查";
        _settingsDialog?.SetUpdateStatus(text, _checkingUpdates, _updateStatus?.ReleasePage is not null);
        string? availableVersion = _updateStatus is { UpdateAvailable: true } status ? status.LatestVersion : null;
        _window?.SetAvailableUpdate(availableVersion);
        _tray?.SetAvailableUpdate(availableVersion);
    }

    private void OpenReleasePage()
    {
        if (_updateStatus?.ReleasePage is not { } page) return;
        try { Process.Start(new ProcessStartInfo(page.AbsoluteUri) { UseShellExecute = true }); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            _settingsDialog?.SetUpdateStatus("无法打开发布页，请检查默认浏览器。", _checkingUpdates, true);
        }
    }

    private async Task RefreshTaskStatusAsync()
    {
        if (_disposed || Interlocked.Exchange(ref _taskStatusReadActive, 1) != 0) return;
        try
        {
            if (!CanReadTaskStatus)
            {
                _completionHistory.SetSourceUnavailable();
                _taskStatusCoordinator?.Disconnect();
                DisconnectWorkBuddy();
                return;
            }
            await Task.WhenAll(RefreshCodexTaskStatusAsync(), RefreshWorkBuddyTaskStatusAsync());
        }
        finally { Interlocked.Exchange(ref _taskStatusReadActive, 0); }
    }

    private bool CanReadTaskStatus => !_disposed && !_sessionPaused && !_settings.SensorsPaused;

    private async Task RefreshCodexTaskStatusAsync()
    {
        try
        {
            LocalCodexTaskReadResult result = await _taskSource.ReadAsync(_taskStatusCancellation.Token);
            if (CanReadTaskStatus)
            {
                _completionHistory.Apply(result);
                _taskStatusCoordinator?.Apply(result);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (!CanReadTaskStatus) return;
            _completionHistory.SetSourceUnavailable();
            _taskStatusCoordinator?.Disconnect();
            // The bounded source reports ordinary missing/malformed data itself.
            // Unexpected errors are recorded without session text or account identifiers.
            _storage.WriteError("local-task-status", new InvalidOperationException(ex.GetType().Name));
        }
    }

    private async Task RefreshWorkBuddyTaskStatusAsync()
    {
        try
        {
            WorkBuddyTaskReadResult result = await _workBuddyTaskSource.ReadAsync(_taskStatusCancellation.Token);
            if (!CanReadTaskStatus) return;
            _workBuddyTaskStatusCoordinator?.Apply(result);
            if (result.CompletionEventsAvailable && result.ScopeKey is { } scope)
                _workBuddyCompletionHistory.Apply(scope, result.CompletionEvents);
            else
                _workBuddyCompletionHistory.SetSourceUnavailable();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (!CanReadTaskStatus) return;
            DisconnectWorkBuddy();
            _storage.WriteError("workbuddy-task-status", new InvalidOperationException(ex.GetType().Name));
        }
    }

    private void DisconnectWorkBuddy()
    {
        _workBuddyTaskStatusCoordinator?.Disconnect();
        _workBuddyCompletionHistory.SetSourceUnavailable();
    }

    private void ConfigureCodexUsageLifecycle(bool refreshImmediately)
    {
        _codexUsageTimer.Interval = TimeSpan.FromMinutes(_settings.CodexUsageRefreshMinutes);
        _codexUsageBadge?.ApplySettings(_settings);
        if (_settings.ShowCodexUsage)
        {
            if (!_codexUsageTimer.IsEnabled)
            {
                _codexUsageTimer.Start();
            }
            if (refreshImmediately)
            {
                _codexUsageBadge?.SetSnapshot(CodexUsageSnapshot.Loading());
                _ = RefreshCodexUsageAsync();
            }
        }
        else
        {
            _codexUsageTimer.Stop();
            _codexUsageRefreshCts?.Cancel();
        }
    }

    private async Task RefreshCodexUsageAsync()
    {
        if (_disposed || !_settings.ShowCodexUsage || Interlocked.Exchange(ref _codexUsageRefreshActive, 1) != 0)
        {
            return;
        }

        _codexUsageRefreshCts?.Dispose();
        var readCancellation = new CancellationTokenSource();
        CancellationToken readToken = readCancellation.Token;
        _codexUsageRefreshCts = readCancellation;
        try
        {
            CodexUsageSnapshot snapshot = await _codexUsageService.ReadAsync(readToken);
            // The compatibility reader converts cancellation into an unavailable snapshot.
            // Reject that cancelled read before it can become a completed local check.
            readToken.ThrowIfCancellationRequested();
            if (!_disposed && _settings.ShowCodexUsage)
            {
                _codexUsageBadge?.SetSnapshot(snapshot, checkedAt: DateTimeOffset.Now);
                _tray?.UpdateCodexUsage(snapshot);
            }
        }
        catch (OperationCanceledException) when (readToken.IsCancellationRequested) { }
        catch (Exception ex)
        {
            _storage.WriteError("codex-usage-refresh", ex);
            if (!_disposed && _settings.ShowCodexUsage)
            {
                CodexUsageSnapshot snapshot = CodexUsageSnapshot.Unavailable("Codex 用量暂不可用；稍后会自动重试。");
                _codexUsageBadge?.SetSnapshot(snapshot);
                _tray?.UpdateCodexUsage(snapshot);
            }
        }
        finally
        {
            Interlocked.Exchange(ref _codexUsageRefreshActive, 0);
            // A quick hide/re-enable may have requested a refresh while the old read
            // was still active. Start its replacement now, rather than wait 5 minutes.
            if (readToken.IsCancellationRequested && !_disposed && _settings.ShowCodexUsage)
                _ = RefreshCodexUsageAsync();
        }
    }

    private void OnInputSampled(object? sender, InputActivitySnapshot e)
    {
        if (e.KeyboardEvents > 0)
        {
            _state.KeyboardActivityBuckets++;
        }
        if (e.MouseEvents > 0)
        {
            _state.MouseActivityBuckets++;
        }
        _behavior?.HandleInput(e);
    }

    private void OnForegroundChanged(object? sender, WindowActivity e)
    {
        _codexWindowTargetTracker?.ObserveForeground(e);
        _state.WindowSwitchCount++;
        _behavior?.HandleWindow(e, false);
        AppendWindowHistory("foreground-changed", e);
    }

    private void OnWindowOpened(object? sender, WindowActivity e)
    {
        _state.WindowOpenCount++;
        _behavior?.HandleWindow(e, true);
        AppendWindowHistory("window-opened", e);
    }

    private void AppendWindowHistory(string type, WindowActivity window)
    {
        _storage.AppendHistory(_settings, new EventHistoryRecord
        {
            Type = type,
            Category = BehaviorController.CategorizeWindow(window.ProcessName, window.Title),
            Process = window.ProcessName,
            WindowTitle = _settings.StoreWindowTitles ? window.Title : null,
            Detail = window.IsFullscreen ? "fullscreen" : null
        });
    }

    private void OnSessionChanged(object? sender, SessionActivity e)
    {
        bool previousPaused = _sessionPaused;
        switch (e.State)
        {
            case "locked":
                _sessionLocked = true;
                break;
            case "unlocked":
                _sessionLocked = false;
                break;
            case "suspend":
                _systemSuspended = true;
                break;
            case "resume":
                _systemSuspended = false;
                break;
            case "disconnected":
                _sessionDisconnected = true;
                break;
            case "connected":
                _sessionDisconnected = false;
                break;
            default:
                return;
        }

        _sessionPaused = _sessionLocked || _systemSuspended || _sessionDisconnected;
        if (_sessionPaused == previousPaused)
        {
            return;
        }

        _behavior?.SetLocked(_sessionPaused);
        if (_sessionPaused)
        {
            _window?.SetFullscreenHidden(false);
        }
        _activityMonitor?.SetSessionPaused(_sessionPaused);
        _window?.SetSessionPaused(_sessionPaused);
        UpdateTaskObservationPause();
        UpdateAudioMonitoring();

        if (_sessionPaused)
        {
            PersistNow();
        }
    }

    private bool ShouldMonitorAudio() =>
        !_settings.SensorsPaused && !_sessionPaused && _settings.MonitorAudio;

    private void UpdateAudioMonitoring()
    {
        bool enabled = ShouldMonitorAudio();
        _audioMonitor.SetEnabled(enabled);
        if (!enabled)
        {
            ResetAudioActivity();
        }
    }

    private void ResetAudioActivity()
    {
        _audioHighSamples = 0;
        _audioLowSamples = 0;
        if (_audioActive)
        {
            _audioActive = false;
            _behavior?.HandleAudioChanged(false);
        }
    }

    private void UpdateSensorLifecycle()
    {
        UpdateTaskObservationPause();
        _activityMonitor?.SetSessionPaused(_sessionPaused);
        UpdateAudioMonitoring();
        bool windowMonitoringEnabled =
            !_settings.SensorsPaused && !_sessionPaused && _settings.MonitorWindows;
        if (!windowMonitoringEnabled)
        {
            _window?.SetFullscreenHidden(false);
        }
        else if (_settings.HideInFullscreen)
        {
            _activityMonitor?.RefreshFullscreen();
        }
    }

    private void UpdateTaskObservationPause()
    {
        bool paused = _sessionPaused || _settings.SensorsPaused;
        _completionHistory.SetObservationPaused(paused);
        _workBuddyCompletionHistory.SetObservationPaused(paused);
        if (paused) DisconnectWorkBuddy();
    }

    private void OnVisualStateChanged(object? sender, VisualStateChangedEventArgs e)
    {
        _window?.SetVisual(e.Animation, e.LookIndex, e.Hint,
            e.LoopOverride, e.StartFrameOverride, e.EndFrameOverride);
    }

    private void OnInteractionRequested(object? sender, bool energetic)
    {
        _state.InteractionCount++;
        _state.Affection++;
        _state.Mood = Math.Min(100, _state.Mood + 1);
        _behavior?.HandleDirectInteraction(energetic);
    }

    private void OnDragged(object? sender, bool rightward)
    {
        // The drag lifecycle owns P1; releasing must not start an unrelated running clip.
    }

    private void OnDraggingChanged(bool dragging) => _behavior?.SetDragging(dragging);
    private void OnDragMotionChanged(double dx, double dy) => _behavior?.HandleDragMotion(dx, dy);

    private void OnCompletionHistoryRequested(object? sender, EventArgs e)
    {
        if (_completionHistoryWindow is { IsVisible: true }) { _completionHistoryWindow.Activate(); return; }
        _completionHistoryWindow = new CompletionHistoryWindow(_completionHistory.Read, _workBuddyCompletionHistory.Read)
        {
            Owner = _window, Topmost = _settings.AlwaysOnTop,
            WindowStartupLocation = WindowStartupLocation.CenterScreen
        };
        _completionHistoryWindow.Closed += (_, _) => _completionHistoryWindow = null;
        _completionHistoryWindow.Show();
    }

    private void OnComfyUiStatusRequested(object? sender, EventArgs e)
    {
        _comfyUiStatusWindow?.Reveal();
    }

    private void OnComfyUiPanelPlacementChanged(ComfyUiPanelPlacement placement)
    {
        _state.ComfyUiPanel = placement;
        _storage.SaveState(_state);
    }

    private void ShowSettings()
    {
        if (_window is null)
        {
            return;
        }

        var dialog = new SettingsWindow(_settings, _storage.RootDirectory)
        {
            Topmost = true,
            WindowStartupLocation = WindowStartupLocation.CenterScreen
        };
        dialog.ClearDataRequested += (_, _) =>
        {
            _storage.DeleteHistoryAndLogs();
            _state = new LumiPersistentState();
            _storage.SaveState(_state);
        };
        dialog.OpenDataDirectoryRequested += (_, _) =>
        {
            try
            {
                Process.Start(new ProcessStartInfo(_storage.RootDirectory) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                _storage.WriteError("open-data-directory", ex);
            }
        };

        dialog.UpdateCheckRequested += (_, _) => _ = CheckForUpdatesAsync();
        dialog.ReleasePageRequested += (_, _) => OpenReleasePage();
        _settingsDialog = dialog;
        RefreshUpdateDisplay();
        try
        {
            if (dialog.ShowDialog() == true) ApplySettings(dialog.ResultSettings, true);
        }
        finally { _settingsDialog = null; }
    }

    private void ApplySettings(LumiSettings updated, bool updateStartup)
    {
        updated.Normalize();
        bool usageRefreshChanged = updated.ShowCodexUsage && (!_settings.ShowCodexUsage ||
            updated.CodexUsageRefreshMinutes != _settings.CodexUsageRefreshMinutes);
        if (!StartupManager.IsSupported)
        {
            updated.StartWithWindows = false;
        }
        else if (updateStartup && updated.StartWithWindows != StartupManager.IsEnabled())
        {
            if (!StartupManager.SetEnabled(updated.StartWithWindows))
            {
                updated.StartWithWindows = StartupManager.IsEnabled();
                MessageBox.Show("无法更新当前用户的开机启动项，其余设置已保存。", "Lumi", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        _settings = updated;
        _completionHistory.SetEnabled(_settings.SaveLocalStats);
        _workBuddyCompletionHistory.SetEnabled(_settings.SaveLocalStats);
        _window?.ApplySettings(_settings);
        _taskStatusBubble?.ApplySettings(_settings);
        _comfyUiStatusWindow?.ApplySettings(_settings);
        _activityMonitor?.UpdateSettings(_settings);
        UpdateSensorLifecycle();
        _behavior?.UpdateSettings(_settings);
        _tray?.UpdateSettings(_settings);
        ConfigureCodexUsageLifecycle(refreshImmediately: usageRefreshChanged);
        _storage.SaveSettings(_settings);
        _storage.PruneHistory(_settings.HistoryRetentionDays);
    }

    private void SaveWindowPosition()
    {
        if (_window is null || !double.IsFinite(_window.Left) || !double.IsFinite(_window.Top))
        {
            return;
        }

        _settings.PositionX = _window.Left;
        _settings.PositionY = _window.Top;
        _storage.SaveSettings(_settings);
    }

    private void PersistNow()
    {
        SaveWindowPosition();
        if (_settings.SaveLocalStats)
        {
            _storage.SaveState(_state);
        }
    }

    public void Exit()
    {
        if (_exiting)
        {
            return;
        }

        _exiting = true;
        PersistNow();
        Dispose();
        _window?.RequestExit();
        _application.Shutdown();
    }

    private void OnApplicationExit(object sender, ExitEventArgs e)
    {
        if (!_exiting)
        {
            PersistNow();
            Dispose();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _behaviorTimer.Stop();
        _audioTimer.Stop();
        _persistenceTimer.Stop();
        _codexUsageTimer.Stop();
        _taskStatusTimer.Stop();
        _taskStatusTimer.Tick -= OnTaskStatusTimerTick;
        _updateTimer.Stop();
        _updateTimer.Tick -= OnUpdateTimerTick;
        _updateCancellation.Cancel();
        _updateCancellation.Dispose();
        _taskStatusCancellation.Cancel();
        _taskStatusCancellation.Dispose();
        _behaviorTimer.Tick -= OnBehaviorTick;
        _audioTimer.Tick -= OnAudioTick;
        _persistenceTimer.Tick -= OnPersistenceTick;
        _codexUsageTimer.Tick -= OnCodexUsageTimerTick;
        _codexUsageRefreshCts?.Cancel();
        _codexUsageRefreshCts?.Dispose();
        _codexUsageRefreshCts = null;

        if (_activityMonitor is not null)
        {
            _activityMonitor.InputSampled -= OnInputSampled;
            _activityMonitor.ForegroundChanged -= OnForegroundChanged;
            _activityMonitor.WindowOpened -= OnWindowOpened;
            _activityMonitor.FullscreenChanged -= OnFullscreenChanged;
            _activityMonitor.SessionChanged -= OnSessionChanged;
            _activityMonitor.Dispose();
        }
        _codexWindowTargetTracker = null;

        if (_tray is not null)
        {
            _tray.ToggleVisibilityRequested -= OnToggleVisibilityRequested;
            _tray.InteractionRequested -= OnTrayInteractionRequested;
            _tray.SettingsRequested -= OnSettingsRequested;
            _tray.CompletionHistoryRequested -= OnCompletionHistoryRequested;
            _tray.ComfyUiStatusRequested -= OnComfyUiStatusRequested;
            _tray.PauseToggled -= OnPauseToggled;
            _tray.ClickThroughToggled -= OnClickThroughToggled;
            _tray.StartupToggled -= OnStartupToggled;
            _tray.CodexUsageVisibilityToggled -= OnCodexUsageVisibilityToggled;
            _tray.CodexUsageRefreshRequested -= OnCodexUsageRefreshRequested;
            _tray.ExitRequested -= OnExitRequested;
            _tray.Dispose();
            _tray = null;
        }

        _completionHistoryWindow?.Close();
        _completionHistoryWindow = null;
        if (_comfyUiStatusWindow is not null)
        {
            _comfyUiStatusWindow.PlacementChanged -= OnComfyUiPanelPlacementChanged;
            _comfyUiStatusWindow.Shutdown();
        }
        _comfyUiStatusWindow = null;
        _taskStatusBubble?.Dispose();
        _taskStatusBubble = null;
        if (_codexUsageBadge is not null)
        {
            _codexUsageBadge.ModuleVisibilityRequested -= OnUsageModuleVisibilityRequested;
            _codexUsageBadge.Dispose();
        }
        _codexUsageBadge = null;

        if (_window is not null)
        {
            _window.AnimationCompleted -= OnAnimationCompleted;
            _window.DraggingChanged -= OnDraggingChanged;
            _window.DragMotionChanged -= OnDragMotionChanged;
            _window.InteractionRequested -= OnInteractionRequested;
            _window.Dragged -= OnDragged;
            _window.ContextMenuRequested -= OnContextMenuRequested;
            _window.PositionChangedByUser -= OnPositionChangedByUser;
            _window.WidthChangedByUser -= OnWidthChangedByUser;
            _window.Dispose();
        }

        _audioMonitor.SetEnabled(false);
        _audioMonitor.Dispose();
        if (_behavior is not null)
        {
            _behavior.VisualStateChanged -= OnVisualStateChanged;
        }
        _application.Exit -= OnApplicationExit;
    }
}


