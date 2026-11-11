/*
Traceability metadata
Version: 0.1.0
Generated: 2026-07-25 (Asia/Shanghai)
Last updated: 2026-07-25
Content coverage through: 2026-07-25
Source type: LOCAL-WORK
Source: Codex Desktop / workspace new-chat / current Lumi independent companion task / Codex
*/

using Microsoft.Win32;

namespace LumiCompanion.Infrastructure;

public static class StartupManager
{
    private const string RegistryPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "LumiCompanion";

    public static bool IsSupported => !StorageService.IsPortable;

    public static bool IsEnabled()
    {
        if (!IsSupported) return false;
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RegistryPath, false);
            return key?.GetValue(ValueName) is string value && !string.IsNullOrWhiteSpace(value);
        }
        catch
        {
            return false;
        }
    }

    public static bool SetEnabled(bool enabled)
    {
        if (!IsSupported) return false;
        try
        {
            using RegistryKey? key = Registry.CurrentUser.CreateSubKey(RegistryPath, true);
            if (key is null)
            {
                return false;
            }

            if (enabled)
            {
                string? path = Environment.ProcessPath;
                if (string.IsNullOrWhiteSpace(path))
                {
                    return false;
                }

                key.SetValue(ValueName, "\"" + path + "\" --autostart", RegistryValueKind.String);
            }
            else
            {
                key.DeleteValue(ValueName, false);
            }

            return true;
        }
        catch
        {
            return false;
        }
    }
}
