/*
Traceability metadata
Version: 0.2.0
Generated: 2026-07-25 (Asia/Shanghai)
Last updated: 2026-07-25
Content coverage through: 2026-07-25
Source type: LOCAL-WORK
Source: Codex Desktop / workspace new-chat / current Lumi independent companion task / Codex
*/

using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Threading;
using LumiCompanion.Models;
using LumiCompanion.Native;
using LumiCompanion.Windowing;
using Microsoft.Win32;

namespace LumiCompanion.Sensors;

public sealed class InputActivitySnapshot : EventArgs
{
    public double IdleSeconds { get; init; }
    public int KeyboardEvents { get; init; }
    public int MouseEvents { get; init; }
    public bool ActivitySinceLastSample => KeyboardEvents > 0 || MouseEvents > 0 || IdleSeconds < 0.6;
}

public sealed class WindowActivity : EventArgs
{
    public IntPtr Handle { get; init; }
    public string ProcessName { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string ExecutablePath { get; init; } = string.Empty;
    public string PackageFullName { get; init; } = string.Empty;
    public PixelRect Bounds { get; init; }
    public bool IsMinimized { get; init; }
    public bool IsFullscreen { get; init; }
}

public sealed class SessionActivity : EventArgs
{
    public string State { get; init; } = string.Empty;
}

public sealed class DesktopActivityMonitor : IDisposable
{
    private readonly HwndSource _source;
    private readonly IntPtr _ownWindow;
    private readonly string _ownProcessName;
    private readonly DispatcherTimer _timer;
    private readonly object _gate = new();
    private readonly CancellationTokenSource _disposeCancellation = new();
    private readonly HashSet<(IntPtr Handle, long Epoch)> _pendingWindowOpens = new();
    private LumiSettings _settings;
    private uint _shellMessage;
    private int _keyboardEvents;
    private int _mouseEvents;
    private IntPtr _lastForeground;
    private string _lastForegroundKey = string.Empty;
    private IntPtr _candidateForeground;
    private string _candidateKey = string.Empty;
    private DateTimeOffset _candidateSince;
    private bool _lastFullscreen;
    private bool _sessionPaused;
    private bool _shellHookRegistered;
    private bool _windowsMonitoringActive;
    private long _windowEventEpoch;
    private int _acceptWindowEvents;
    private bool _rawInputRegistered;
    private int _captureRawInput;
    private int _disposed;

    public DesktopActivityMonitor(HwndSource source, LumiSettings settings)
    {
        _source = source;
        _ownWindow = source.Handle;
        _ownProcessName = GetCurrentProcessName();
        _settings = settings;
        _source.AddHook(WindowProc);
        _shellMessage = NativeMethods.RegisterWindowMessage("SHELLHOOK");

        SystemEvents.SessionSwitch += OnSessionSwitch;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;

        _timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(250)
        };
        _timer.Tick += OnTick;
        UpdateMonitoringState();
    }

    public event EventHandler<InputActivitySnapshot>? InputSampled;
    public event EventHandler<WindowActivity>? ForegroundChanged;
    public event EventHandler<WindowActivity>? WindowOpened;
    public event EventHandler<bool>? FullscreenChanged;
    public event EventHandler<SessionActivity>? SessionChanged;

    public void UpdateSettings(LumiSettings settings)
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        lock (_gate)
        {
            _settings = settings;
        }

        UpdateMonitoringState();
    }

    public void SetSessionPaused(bool paused)
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        lock (_gate)
        {
            if (_sessionPaused == paused)
            {
                return;
            }
            _sessionPaused = paused;
        }

        UpdateMonitoringState();
    }

    public void RefreshFullscreen()
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        LumiSettings settings;
        long epoch;
        lock (_gate)
        {
            settings = _settings;
            if (_sessionPaused || settings.SensorsPaused || !settings.MonitorWindows ||
                !_windowsMonitoringActive)
            {
                return;
            }
            epoch = _windowEventEpoch;
        }

        IntPtr foreground = NativeMethods.GetForegroundWindow();
        bool fullscreen = GetWindowActivity(foreground, includeTitle: false)?.IsFullscreen ?? false;

        lock (_gate)
        {
            if (_sessionPaused || _settings.SensorsPaused || !_settings.MonitorWindows ||
                !_windowsMonitoringActive || epoch != _windowEventEpoch)
            {
                return;
            }

            _lastFullscreen = fullscreen;
        }

        FullscreenChanged?.Invoke(this, fullscreen);
    }

    public static bool TryGetCursorPosition(out System.Windows.Point point)
    {
        if (NativeMethods.GetCursorPos(out NativeMethods.POINT nativePoint))
        {
            point = new System.Windows.Point(nativePoint.X, nativePoint.Y);
            return true;
        }

        point = default;
        return false;
    }

    public static double GetIdleSeconds()
    {
        var info = new NativeMethods.LASTINPUTINFO
        {
            cbSize = (uint)Marshal.SizeOf<NativeMethods.LASTINPUTINFO>()
        };
        if (!NativeMethods.GetLastInputInfo(ref info))
        {
            return 0;
        }

        uint now = unchecked((uint)Environment.TickCount64);
        uint elapsed = unchecked(now - info.dwTime);
        return elapsed / 1000.0;
    }

    private void OnTick(object? sender, EventArgs e)
    {
        LumiSettings settings;
        bool sessionPaused;
        lock (_gate)
        {
            settings = _settings;
            sessionPaused = _sessionPaused;
        }

        if (sessionPaused || settings.SensorsPaused)
        {
            ClearRawInputCounters();
            return;
        }

        if (settings.MonitorInputActivity)
        {
            int keyboard = Interlocked.Exchange(ref _keyboardEvents, 0);
            int mouse = Interlocked.Exchange(ref _mouseEvents, 0);
            if (!settings.DistinguishKeyboardAndMouse)
            {
                mouse += keyboard;
                keyboard = 0;
            }

            InputSampled?.Invoke(this, new InputActivitySnapshot
            {
                IdleSeconds = GetIdleSeconds(),
                KeyboardEvents = keyboard,
                MouseEvents = mouse
            });
        }
        else
        {
            ClearRawInputCounters();
        }

        if (settings.MonitorWindows)
        {
            PollForeground(settings);
        }
    }

    private void PollForeground(LumiSettings settings)
    {
        IntPtr current = NativeMethods.GetForegroundWindow();
        WindowActivity? info = GetWindowActivity(current, settings.ReadWindowTitles);
        if (info is null)
        {
            return;
        }

        string key = info.Handle.ToInt64() + "|" + info.ProcessName + "|" + info.Title;
        if (current != _candidateForeground || !string.Equals(key, _candidateKey, StringComparison.Ordinal))
        {
            _candidateForeground = current;
            _candidateKey = key;
            _candidateSince = DateTimeOffset.Now;
            return;
        }

        if (DateTimeOffset.Now - _candidateSince < TimeSpan.FromMilliseconds(350))
        {
            return;
        }

        if (current != _lastForeground || !string.Equals(key, _lastForegroundKey, StringComparison.Ordinal))
        {
            _lastForeground = current;
            _lastForegroundKey = key;
            ForegroundChanged?.Invoke(this, info);
        }

        if (info.IsFullscreen != _lastFullscreen)
        {
            _lastFullscreen = info.IsFullscreen;
            FullscreenChanged?.Invoke(this, info.IsFullscreen);
        }
    }

    private IntPtr WindowProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            return IntPtr.Zero;
        }

        if (message == NativeMethods.WM_INPUT)
        {
            if (Volatile.Read(ref _captureRawInput) != 0)
            {
                ReadRawInputType(lParam);
            }
            return IntPtr.Zero;
        }

        if (_shellMessage != 0 && message == _shellMessage && Volatile.Read(ref _acceptWindowEvents) != 0)
        {
            int shellCode = wParam.ToInt32();
            if (shellCode == NativeMethods.HSHELL_WINDOWCREATED)
            {
                QueueWindowOpened(lParam);
            }
        }

        return IntPtr.Zero;
    }

    private void QueueWindowOpened(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero || Volatile.Read(ref _disposed) != 0 ||
            Volatile.Read(ref _acceptWindowEvents) == 0)
        {
            return;
        }

        long epoch;
        (IntPtr Handle, long Epoch) pending;
        lock (_gate)
        {
            epoch = _windowEventEpoch;
            pending = (hwnd, epoch);
            if (_disposed != 0 || _sessionPaused || _settings.SensorsPaused ||
                !_settings.MonitorWindows || Volatile.Read(ref _acceptWindowEvents) == 0 ||
                !_pendingWindowOpens.Add(pending))
            {
                return;
            }
        }

        _ = ProcessWindowOpenedAsync(hwnd, epoch, _disposeCancellation.Token);
    }

    private async Task ProcessWindowOpenedAsync(IntPtr hwnd, long epoch, CancellationToken cancellationToken)
    {
        (IntPtr Handle, long Epoch) pending = (hwnd, epoch);
        try
        {
            await Task.Delay(500, cancellationToken).ConfigureAwait(true);
            if (cancellationToken.IsCancellationRequested || Volatile.Read(ref _disposed) != 0)
            {
                return;
            }

            LumiSettings settings;
            bool active;
            lock (_gate)
            {
                settings = _settings;
                active = !_sessionPaused && !settings.SensorsPaused && settings.MonitorWindows &&
                    epoch == _windowEventEpoch;
            }

            if (!active)
            {
                return;
            }

            WindowActivity? info = GetWindowActivity(hwnd, settings.ReadWindowTitles);
            lock (_gate)
            {
                active = !_sessionPaused && !_settings.SensorsPaused && _settings.MonitorWindows &&
                    epoch == _windowEventEpoch;
            }
            if (info is not null && active && !cancellationToken.IsCancellationRequested &&
                Volatile.Read(ref _disposed) == 0)
            {
                WindowOpened?.Invoke(this, info);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Disposal cancels delayed shell notifications.
        }
        catch
        {
            // A vanished window is expected during shell event bursts.
        }
        finally
        {
            lock (_gate)
            {
                _pendingWindowOpens.Remove(pending);
            }
        }
    }

    private void ReadRawInputType(IntPtr rawInput)
    {
        uint headerSize = (uint)Marshal.SizeOf<NativeMethods.RAWINPUTHEADER>();
        uint size = headerSize;
        uint returned = NativeMethods.GetRawInputData(
            rawInput,
            NativeMethods.RID_HEADER,
            out NativeMethods.RAWINPUTHEADER header,
            ref size,
            headerSize);
        if (returned == uint.MaxValue || returned < headerSize)
        {
            return;
        }

        if (header.dwType == NativeMethods.RIM_TYPEKEYBOARD)
        {
            Interlocked.Increment(ref _keyboardEvents);
        }
        else if (header.dwType == NativeMethods.RIM_TYPEMOUSE)
        {
            Interlocked.Increment(ref _mouseEvents);
        }
    }

    private void UpdateMonitoringState()
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        LumiSettings settings;
        bool sessionPaused;
        lock (_gate)
        {
            settings = _settings;
            sessionPaused = _sessionPaused;
        }

        bool sensorsEnabled = !sessionPaused && !settings.SensorsPaused;
        bool shouldCaptureInput = sensorsEnabled && settings.MonitorInputActivity;
        bool shouldMonitorWindows = sensorsEnabled && settings.MonitorWindows;
        UpdateRawInputRegistration(shouldCaptureInput);
        UpdateShellHookRegistration(shouldMonitorWindows);

        if (shouldCaptureInput || shouldMonitorWindows)
        {
            if (!_timer.IsEnabled)
            {
                _timer.Start();
            }
        }
        else
        {
            _timer.Stop();
        }
    }

    private void UpdateRawInputRegistration(bool shouldCapture)
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        if (shouldCapture)
        {
            if (!_rawInputRegistered)
            {
                _rawInputRegistered = RegisterPrivacySafeRawInput();
            }

            Volatile.Write(ref _captureRawInput, _rawInputRegistered ? 1 : 0);
            return;
        }

        Volatile.Write(ref _captureRawInput, 0);
        ClearRawInputCounters();
        if (_rawInputRegistered && UnregisterPrivacySafeRawInput())
        {
            _rawInputRegistered = false;
        }
    }

    private void UpdateShellHookRegistration(bool shouldMonitor)
    {
        bool becameActive = false;
        if (shouldMonitor)
        {
            if (!_windowsMonitoringActive)
            {
                becameActive = true;
                Interlocked.Increment(ref _windowEventEpoch);
                lock (_gate)
                {
                    _pendingWindowOpens.Clear();
                }
                ResetWindowPollingState();
                _windowsMonitoringActive = true;
            }

            if (!_shellHookRegistered && _shellMessage != 0)
            {
                _shellHookRegistered = NativeMethods.RegisterShellHookWindow(_ownWindow);
            }

            Volatile.Write(ref _acceptWindowEvents, _shellHookRegistered ? 1 : 0);
            if (becameActive)
            {
                RefreshFullscreen();
            }
            return;
        }

        Volatile.Write(ref _acceptWindowEvents, 0);
        bool wasActive = _windowsMonitoringActive;
        if (_windowsMonitoringActive)
        {
            Interlocked.Increment(ref _windowEventEpoch);
            lock (_gate)
            {
                _pendingWindowOpens.Clear();
            }
            ResetWindowPollingState();
            _windowsMonitoringActive = false;
        }

        if (_shellHookRegistered && NativeMethods.DeregisterShellHookWindow(_ownWindow))
        {
            _shellHookRegistered = false;
        }

        if (wasActive)
        {
            FullscreenChanged?.Invoke(this, false);
        }
    }

    private void ResetWindowPollingState()
    {
        _lastForeground = IntPtr.Zero;
        _lastForegroundKey = string.Empty;
        _candidateForeground = IntPtr.Zero;
        _candidateKey = string.Empty;
        _candidateSince = default;
        _lastFullscreen = false;
    }

    private bool RegisterPrivacySafeRawInput()
    {
        var devices = new[]
        {
            new NativeMethods.RAWINPUTDEVICE
            {
                usUsagePage = 0x01,
                usUsage = 0x02,
                dwFlags = NativeMethods.RIDEV_INPUTSINK,
                hwndTarget = _ownWindow
            },
            new NativeMethods.RAWINPUTDEVICE
            {
                usUsagePage = 0x01,
                usUsage = 0x06,
                dwFlags = NativeMethods.RIDEV_INPUTSINK,
                hwndTarget = _ownWindow
            }
        };
        return NativeMethods.RegisterRawInputDevices(
            devices,
            (uint)devices.Length,
            (uint)Marshal.SizeOf<NativeMethods.RAWINPUTDEVICE>());
    }

    private static bool UnregisterPrivacySafeRawInput()
    {
        var devices = new[]
        {
            new NativeMethods.RAWINPUTDEVICE
            {
                usUsagePage = 0x01,
                usUsage = 0x02,
                dwFlags = NativeMethods.RIDEV_REMOVE,
                hwndTarget = IntPtr.Zero
            },
            new NativeMethods.RAWINPUTDEVICE
            {
                usUsagePage = 0x01,
                usUsage = 0x06,
                dwFlags = NativeMethods.RIDEV_REMOVE,
                hwndTarget = IntPtr.Zero
            }
        };
        return NativeMethods.RegisterRawInputDevices(
            devices,
            (uint)devices.Length,
            (uint)Marshal.SizeOf<NativeMethods.RAWINPUTDEVICE>());
    }

    private void ClearRawInputCounters()
    {
        Interlocked.Exchange(ref _keyboardEvents, 0);
        Interlocked.Exchange(ref _mouseEvents, 0);
    }

    private WindowActivity? GetWindowActivity(IntPtr hwnd, bool includeTitle)
    {
        if (hwnd == IntPtr.Zero || hwnd == _ownWindow || !NativeMethods.IsWindowVisible(hwnd))
        {
            return null;
        }

        if (NativeMethods.GetAncestor(hwnd, NativeMethods.GA_ROOT) != hwnd)
        {
            return null;
        }

        try
        {
            if (NativeMethods.DwmGetWindowAttribute(hwnd, NativeMethods.DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0 && cloaked != 0)
            {
                return null;
            }
        }
        catch
        {
            // DWM is optional for validation; visibility and root-window checks remain.
        }

        NativeMethods.GetWindowThreadProcessId(hwnd, out uint processId);
        string processName;
        string executablePath = string.Empty;
        try
        {
            using Process process = Process.GetProcessById((int)processId);
            processName = process.ProcessName;
            try
            {
                executablePath = process.MainModule?.FileName ?? string.Empty;
            }
            catch
            {
                // Packaged apps may deny MainModule access; package identity remains available.
            }
        }
        catch
        {
            return null;
        }

        if (string.Equals(processName, _ownProcessName, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (!NativeMethods.GetWindowRect(hwnd, out NativeMethods.RECT rect))
        {
            return null;
        }

        string title = includeTitle ? ReadWindowTitle(hwnd) : string.Empty;
        return new WindowActivity
        {
            Handle = hwnd,
            ProcessName = processName,
            Title = title,
            ExecutablePath = executablePath,
            PackageFullName = WindowIdentityProbe.TryGetPackageFullName(processId),
            Bounds = new PixelRect(rect.Left, rect.Top, rect.Right, rect.Bottom),
            IsMinimized = NativeMethods.IsIconic(hwnd),
            IsFullscreen = IsFullscreenWindow(hwnd, processName)
        };
    }

    private static string GetCurrentProcessName()
    {
        using Process process = Process.GetCurrentProcess();
        return process.ProcessName;
    }

    private static string ReadWindowTitle(IntPtr hwnd)
    {
        int length = Math.Min(Math.Max(NativeMethods.GetWindowTextLengthW(hwnd), 0), 511);
        if (length == 0)
        {
            return string.Empty;
        }

        var builder = new System.Text.StringBuilder(length + 1);
        NativeMethods.GetWindowTextW(hwnd, builder, builder.Capacity);
        return builder.ToString();
    }

    private static bool IsFullscreenWindow(IntPtr hwnd, string processName)
    {
        if (string.Equals(processName, "explorer", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!NativeMethods.GetWindowRect(hwnd, out NativeMethods.RECT windowRect))
        {
            return false;
        }

        IntPtr monitor = NativeMethods.MonitorFromWindow(hwnd, NativeMethods.MONITOR_DEFAULTTONEAREST);
        var monitorInfo = new NativeMethods.MONITORINFO
        {
            cbSize = (uint)Marshal.SizeOf<NativeMethods.MONITORINFO>()
        };
        if (!NativeMethods.GetMonitorInfo(monitor, ref monitorInfo))
        {
            return false;
        }

        NativeMethods.RECT screen = monitorInfo.rcMonitor;
        const int tolerance = 2;
        return Math.Abs(windowRect.Left - screen.Left) <= tolerance &&
               Math.Abs(windowRect.Top - screen.Top) <= tolerance &&
               Math.Abs(windowRect.Right - screen.Right) <= tolerance &&
               Math.Abs(windowRect.Bottom - screen.Bottom) <= tolerance;
    }

    private void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
    {
        string state = e.Reason switch
        {
            SessionSwitchReason.SessionLock => "locked",
            SessionSwitchReason.SessionUnlock => "unlocked",
            SessionSwitchReason.ConsoleDisconnect => "disconnected",
            SessionSwitchReason.ConsoleConnect => "connected",
            _ => e.Reason.ToString()
        };
        DispatchSessionChanged(state);
    }

    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        string state = e.Mode switch
        {
            PowerModes.Suspend => "suspend",
            PowerModes.Resume => "resume",
            _ => "power-status-changed"
        };
        DispatchSessionChanged(state);
    }

    private void DispatchSessionChanged(string state)
    {
        if (Volatile.Read(ref _disposed) != 0 || _source.Dispatcher.HasShutdownStarted)
        {
            return;
        }

        _source.Dispatcher.BeginInvoke(() =>
        {
            if (Volatile.Read(ref _disposed) == 0 && !_source.Dispatcher.HasShutdownStarted)
            {
                SessionChanged?.Invoke(this, new SessionActivity { State = state });
            }
        });
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        Volatile.Write(ref _acceptWindowEvents, 0);
        Interlocked.Increment(ref _windowEventEpoch);
        Volatile.Write(ref _captureRawInput, 0);
        ClearRawInputCounters();
        _disposeCancellation.Cancel();
        _timer.Stop();
        _timer.Tick -= OnTick;
        SystemEvents.SessionSwitch -= OnSessionSwitch;
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        if (_rawInputRegistered)
        {
            UnregisterPrivacySafeRawInput();
            _rawInputRegistered = false;
        }
        if (_shellHookRegistered)
        {
            NativeMethods.DeregisterShellHookWindow(_ownWindow);
            _shellHookRegistered = false;
        }
        _source.RemoveHook(WindowProc);
        lock (_gate)
        {
            _pendingWindowOpens.Clear();
        }
        _disposeCancellation.Dispose();
    }
}


