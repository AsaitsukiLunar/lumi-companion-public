/*
Traceability metadata
Version: 0.3.0-dev
Generated: 2026-08-09 (Asia/Shanghai)
Last updated: 2026-08-09
Content coverage through: 2026-08-09
Source type: LOCAL-WORK
Source: Codex Desktop / workspace new-chat / current Lumi independent companion task / Codex
*/

using System.Runtime.InteropServices;
using System.Text;

namespace LumiCompanion.Windowing;

internal static class WindowIdentityProbe
{
    private const uint ProcessQueryLimitedInformation = 0x1000;
    private const int ErrorInsufficientBuffer = 122;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint desiredAccess, bool inheritHandle, uint processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetPackageFullName(IntPtr process, ref uint packageFullNameLength, StringBuilder? packageFullName);

    internal static string TryGetPackageFullName(uint processId)
    {
        IntPtr process = OpenProcess(ProcessQueryLimitedInformation, false, processId);
        if (process == IntPtr.Zero)
        {
            return string.Empty;
        }

        try
        {
            uint length = 0;
            int result = GetPackageFullName(process, ref length, null);
            if (result != ErrorInsufficientBuffer || length == 0 || length > 4096)
            {
                return string.Empty;
            }

            var builder = new StringBuilder((int)length);
            result = GetPackageFullName(process, ref length, builder);
            return result == 0 ? builder.ToString() : string.Empty;
        }
        catch
        {
            return string.Empty;
        }
        finally
        {
            CloseHandle(process);
        }
    }
}
