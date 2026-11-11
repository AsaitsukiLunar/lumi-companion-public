/*
Traceability metadata
Version: 0.2.0
Generated: 2026-07-25 (Asia/Shanghai)
Last updated: 2026-07-25
Content coverage through: 2026-07-25
Source type: LOCAL-WORK
Source: Codex Desktop / workspace new-chat / current Lumi independent companion task / Codex
*/

using System.Runtime.InteropServices;

namespace LumiCompanion.Sensors;

/// <summary>
/// Monitors the peak level of the current Windows default multimedia output endpoint.
/// COM objects stay on a dedicated MTA thread; callers only read cached values.
/// </summary>
public sealed class AudioPeakMonitor : IDisposable
{
    private const int SampleIntervalMilliseconds = 50;
    private const int RetryIntervalMilliseconds = 1_000;
    private const int EndpointCheckIntervalMilliseconds = 2_000;

    private readonly object _lifecycleGate = new();
    private readonly ManualResetEvent _stopSignal = new(initialState: false);
    private readonly AutoResetEvent _stateChangedSignal = new(initialState: false);
    private readonly WaitHandle[] _waitHandles;
    private readonly Thread _monitorThread;

    private int _disposed;
    private int _enabled;
    private int _isAvailable;
    private float _peakValue;

    public AudioPeakMonitor(bool enabled = false)
    {
        _enabled = enabled ? 1 : 0;
        _waitHandles = new WaitHandle[] { _stopSignal, _stateChangedSignal };
        _monitorThread = new Thread(MonitorLoop)
        {
            IsBackground = true,
            Name = "Lumi audio peak monitor",
        };

        _monitorThread.SetApartmentState(ApartmentState.MTA);
        _monitorThread.Start();
    }

    /// <summary>
    /// Gets whether the default output endpoint is currently available for metering.
    /// </summary>
    public bool IsEnabled =>
        Volatile.Read(ref _disposed) == 0 && Volatile.Read(ref _enabled) != 0;

    public bool IsAvailable =>
        IsEnabled && Volatile.Read(ref _isAvailable) != 0;

    /// <summary>
    /// Enables or disables endpoint sampling. Disabling wakes the monitor thread so
    /// that its COM meter and endpoint enumerator are released promptly.
    /// </summary>
    public void SetEnabled(bool enabled)
    {
        lock (_lifecycleGate)
        {
            if (_disposed != 0 || Interlocked.Exchange(ref _enabled, enabled ? 1 : 0) == (enabled ? 1 : 0))
            {
                return;
            }

            if (!enabled)
            {
                SetUnavailable();
            }

            _stateChangedSignal.Set();
        }
    }

    /// <summary>
    /// Returns the latest peak sample in the range 0..1. Failures and unavailable
    /// output devices are represented as 0 rather than exceptions.
    /// </summary>
    public float GetPeakValue()
    {
        if (!IsAvailable)
        {
            return 0f;
        }

        float value = Volatile.Read(ref _peakValue);
        return float.IsFinite(value) ? Math.Clamp(value, 0f, 1f) : 0f;
    }

    public void Dispose()
    {
        lock (_lifecycleGate)
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            Volatile.Write(ref _enabled, 0);
            SetUnavailable();
            _stopSignal.Set();
            _stateChangedSignal.Set();
        }

        if (Thread.CurrentThread != _monitorThread &&
            _monitorThread.Join(TimeSpan.FromSeconds(3)))
        {
            _stopSignal.Dispose();
            _stateChangedSignal.Dispose();
        }

        GC.SuppressFinalize(this);
    }

    private void MonitorLoop()
    {
        try
        {
            MonitorLoopCore();
        }
        catch (COMException)
        {
            SetUnavailable();
        }
        catch (InvalidComObjectException)
        {
            SetUnavailable();
        }
        catch (Exception exception) when (IsNonFatalSensorException(exception))
        {
            // Audio metering is optional. A sensor failure must never terminate Lumi.
            SetUnavailable();
        }
        finally
        {
            SetUnavailable();
        }
    }

    private void MonitorLoopCore()
    {
        int initializeResult = NativeMethods.CoInitializeEx(
            IntPtr.Zero,
            NativeMethods.CoinitMultithreaded);

        if (initializeResult < 0)
        {
            SetUnavailable();
            return;
        }

        IMMDeviceEnumerator? enumerator = null;
        IAudioMeterInformation? meter = null;
        string? endpointId = null;
        long nextEndpointCheck = 0;

        try
        {
            while (Volatile.Read(ref _disposed) == 0)
            {
                if (Volatile.Read(ref _enabled) == 0)
                {
                    SetUnavailable();
                    ReleaseComObject(ref meter);
                    ReleaseComObject(ref enumerator);
                    endpointId = null;
                    nextEndpointCheck = 0;

                    if (WaitForStopOrStateChange(Timeout.Infinite))
                    {
                        break;
                    }
                    continue;
                }

                if (enumerator is null)
                {
                    if (!TryCreateEnumerator(out enumerator) || enumerator is null)
                    {
                        SetUnavailable();
                        if (WaitForStopOrStateChange(RetryIntervalMilliseconds))
                        {
                            break;
                        }

                        continue;
                    }
                }

                IMMDeviceEnumerator activeEnumerator = enumerator;

                if (meter is null)
                {
                    if (!TryCreateMeter(
                            activeEnumerator,
                            out meter,
                            out endpointId) ||
                        meter is null ||
                        string.IsNullOrWhiteSpace(endpointId))
                    {
                        SetUnavailable();
                        ReleaseComObject(ref meter);
                        ReleaseComObject(ref enumerator);
                        endpointId = null;

                        if (WaitForStopOrStateChange(RetryIntervalMilliseconds))
                        {
                            break;
                        }

                        continue;
                    }

                    nextEndpointCheck = Environment.TickCount64 +
                        EndpointCheckIntervalMilliseconds;
                    Volatile.Write(ref _isAvailable, 1);
                }

                long now = Environment.TickCount64;
                if (now >= nextEndpointCheck)
                {
                    if (!TryGetDefaultEndpointId(
                            activeEnumerator,
                            out string? currentEndpointId))
                    {
                        SetUnavailable();
                        ReleaseComObject(ref meter);
                        ReleaseComObject(ref enumerator);
                        endpointId = null;

                        if (WaitForStopOrStateChange(RetryIntervalMilliseconds))
                        {
                            break;
                        }

                        continue;
                    }

                    nextEndpointCheck = now + EndpointCheckIntervalMilliseconds;

                    if (!string.Equals(
                            currentEndpointId,
                            endpointId,
                            StringComparison.Ordinal))
                    {
                        SetUnavailable();
                        ReleaseComObject(ref meter);
                        endpointId = null;
                        continue;
                    }
                }

                IAudioMeterInformation activeMeter = meter;
                if (!TryReadPeak(activeMeter, out float peak))
                {
                    SetUnavailable();
                    ReleaseComObject(ref meter);
                    endpointId = null;

                    if (WaitForStopOrStateChange(RetryIntervalMilliseconds))
                    {
                        break;
                    }

                    continue;
                }

                Volatile.Write(ref _peakValue, peak);
                Volatile.Write(ref _isAvailable, 1);

                if (WaitForStopOrStateChange(SampleIntervalMilliseconds))
                {
                    break;
                }
            }
        }
        finally
        {
            SetUnavailable();
            ReleaseComObject(ref meter);
            ReleaseComObject(ref enumerator);
            NativeMethods.CoUninitialize();
        }
    }

    private bool WaitForStopOrStateChange(int millisecondsTimeout)
    {
        return WaitHandle.WaitAny(_waitHandles, millisecondsTimeout) == 0;
    }

    private static bool TryCreateEnumerator(out IMMDeviceEnumerator? enumerator)
    {
        enumerator = null;
        object? enumeratorObject = null;

        try
        {
            enumeratorObject = new MMDeviceEnumeratorComObject();
            if (enumeratorObject is not IMMDeviceEnumerator audioEnumerator)
            {
                return false;
            }

            enumerator = audioEnumerator;
            enumeratorObject = null;
            return true;
        }
        catch (COMException)
        {
            return false;
        }
        catch (InvalidComObjectException)
        {
            return false;
        }
        catch (InvalidCastException)
        {
            return false;
        }
        finally
        {
            if (enumeratorObject is not null)
            {
                ReleaseComObject(enumeratorObject);
            }
        }
    }

    private static bool TryCreateMeter(
        IMMDeviceEnumerator enumerator,
        out IAudioMeterInformation? meter,
        out string? endpointId)
    {
        meter = null;
        endpointId = null;

        IMMDevice? endpoint = null;
        object? activatedObject = null;
        bool succeeded = false;

        try
        {
            int result = enumerator.GetDefaultAudioEndpoint(
                EDataFlow.Render,
                ERole.Multimedia,
                out endpoint);
            if (result < 0 || endpoint is null)
            {
                return false;
            }

            result = endpoint.GetId(out string currentEndpointId);
            if (result < 0 || string.IsNullOrWhiteSpace(currentEndpointId))
            {
                return false;
            }

            Guid meterInterfaceId = typeof(IAudioMeterInformation).GUID;
            result = endpoint.Activate(
                ref meterInterfaceId,
                ClsContext.All,
                IntPtr.Zero,
                out activatedObject);
            if (result < 0 || activatedObject is not IAudioMeterInformation audioMeter)
            {
                return false;
            }

            meter = audioMeter;
            endpointId = currentEndpointId;
            activatedObject = null;
            succeeded = true;
            return true;
        }
        catch (COMException)
        {
            return false;
        }
        catch (InvalidComObjectException)
        {
            return false;
        }
        finally
        {
            ReleaseComObject(ref endpoint);

            if (activatedObject is not null)
            {
                ReleaseComObject(activatedObject);
            }

            if (!succeeded)
            {
                meter = null;
                endpointId = null;
            }
        }
    }

    private static bool TryGetDefaultEndpointId(
        IMMDeviceEnumerator enumerator,
        out string? endpointId)
    {
        endpointId = null;
        IMMDevice? endpoint = null;

        try
        {
            int result = enumerator.GetDefaultAudioEndpoint(
                EDataFlow.Render,
                ERole.Multimedia,
                out endpoint);
            if (result < 0 || endpoint is null)
            {
                return false;
            }

            result = endpoint.GetId(out string currentEndpointId);
            if (result < 0 || string.IsNullOrWhiteSpace(currentEndpointId))
            {
                return false;
            }

            endpointId = currentEndpointId;
            return true;
        }
        catch (COMException)
        {
            return false;
        }
        catch (InvalidComObjectException)
        {
            return false;
        }
        finally
        {
            ReleaseComObject(ref endpoint);
        }
    }

    private static bool TryReadPeak(
        IAudioMeterInformation meter,
        out float peak)
    {
        peak = 0f;

        try
        {
            int result = meter.GetPeakValue(out float sampledPeak);
            if (result < 0 || !float.IsFinite(sampledPeak))
            {
                return false;
            }

            peak = Math.Clamp(sampledPeak, 0f, 1f);
            return true;
        }
        catch (COMException)
        {
            return false;
        }
        catch (InvalidComObjectException)
        {
            return false;
        }
    }

    private static bool IsNonFatalSensorException(Exception exception) =>
        exception is not OutOfMemoryException and
        not StackOverflowException and
        not AccessViolationException;

    private void SetUnavailable()
    {
        Volatile.Write(ref _peakValue, 0f);
        Volatile.Write(ref _isAvailable, 0);
    }

    private static void ReleaseComObject<T>(ref T? value)
        where T : class
    {
        T? objectToRelease = value;
        value = null;

        if (objectToRelease is not null)
        {
            ReleaseComObject(objectToRelease);
        }
    }

    private static void ReleaseComObject(object value)
    {
        try
        {
            if (Marshal.IsComObject(value))
            {
                Marshal.FinalReleaseComObject(value);
            }
        }
        catch (COMException)
        {
            // A failed audio RCW is already unusable; metering will be marked unavailable.
        }
        catch (InvalidComObjectException)
        {
            // Another failure path already released the RCW.
        }
    }

    private static class NativeMethods
    {
        internal const uint CoinitMultithreaded = 0x0;

        [DllImport("ole32.dll")]
        internal static extern int CoInitializeEx(IntPtr reserved, uint coInit);

        [DllImport("ole32.dll")]
        internal static extern void CoUninitialize();
    }
}

internal enum EDataFlow
{
    Render = 0,
    Capture = 1,
    All = 2,
}

internal enum ERole
{
    Console = 0,
    Multimedia = 1,
    Communications = 2,
}

[Flags]
internal enum ClsContext : uint
{
    InProcessServer = 0x1,
    InProcessHandler = 0x2,
    LocalServer = 0x4,
    RemoteServer = 0x10,
    All = InProcessServer | InProcessHandler | LocalServer | RemoteServer,
}

[ComImport]
[Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
internal sealed class MMDeviceEnumeratorComObject
{
}

[ComImport]
[Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDeviceEnumerator
{
    [PreserveSig]
    int EnumAudioEndpoints(
        EDataFlow dataFlow,
        uint stateMask,
        out IMMDeviceCollection devices);

    [PreserveSig]
    int GetDefaultAudioEndpoint(
        EDataFlow dataFlow,
        ERole role,
        out IMMDevice endpoint);

    [PreserveSig]
    int GetDevice(
        [MarshalAs(UnmanagedType.LPWStr)] string id,
        out IMMDevice device);

    [PreserveSig]
    int RegisterEndpointNotificationCallback(IntPtr notificationClient);

    [PreserveSig]
    int UnregisterEndpointNotificationCallback(IntPtr notificationClient);
}

[ComImport]
[Guid("0BD7A1BE-7A1A-44DB-8397-C0A0D5A8688C")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDeviceCollection
{
    [PreserveSig]
    int GetCount(out uint deviceCount);

    [PreserveSig]
    int Item(uint deviceIndex, out IMMDevice device);
}

[ComImport]
[Guid("D666063F-1587-4E43-81F1-B948E807363F")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDevice
{
    [PreserveSig]
    int Activate(
        ref Guid interfaceId,
        ClsContext classContext,
        IntPtr activationParameters,
        [MarshalAs(UnmanagedType.IUnknown)] out object activatedInterface);

    [PreserveSig]
    int OpenPropertyStore(uint storageMode, out IntPtr propertyStore);

    [PreserveSig]
    int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);

    [PreserveSig]
    int GetState(out uint state);
}

[ComImport]
[Guid("C02216F6-8C67-4B5B-9D00-D008E73E0064")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioMeterInformation
{
    [PreserveSig]
    int GetPeakValue(out float peak);

    [PreserveSig]
    int GetMeteringChannelCount(out uint channelCount);

    [PreserveSig]
    int GetChannelsPeakValues(
        uint channelCount,
        [Out, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)] float[] peakValues);

    [PreserveSig]
    int QueryHardwareSupport(out uint hardwareSupportMask);
}
