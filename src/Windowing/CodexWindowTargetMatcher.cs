/*
Traceability metadata
Version: 0.3.0-dev
Generated: 2026-08-09 (Asia/Shanghai)
Last updated: 2026-08-09
Content coverage through: 2026-08-09
Source type: LOCAL-WORK
Source: Codex Desktop / workspace new-chat / current Lumi independent companion task / Codex
*/

using LumiCompanion.Sensors;

namespace LumiCompanion.Windowing;

public static class CodexWindowTargetMatcher
{
    private const string CodexPackagePrefix = "OpenAI.Codex_";

    public static bool IsMatch(WindowActivity window) => IsMatch(
        window.ProcessName,
        window.Title,
        window.ExecutablePath,
        window.PackageFullName);

    public static bool IsMatch(
        string processName,
        string title,
        string executablePath,
        string packageFullName)
    {
        if (packageFullName.StartsWith(CodexPackagePrefix, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        string normalizedPath = executablePath.Replace('/', '\\');
        if (normalizedPath.Contains("\\OpenAI.Codex_", StringComparison.OrdinalIgnoreCase) ||
            normalizedPath.Contains("\\OpenAI\\Codex\\", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        bool codexTitle = title.Contains("Codex", StringComparison.OrdinalIgnoreCase);
        if (codexTitle &&
            (processName.Equals("ChatGPT", StringComparison.OrdinalIgnoreCase) ||
             processName.Equals("Codex", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        // A visible top-level `codex` window is accepted only with a Codex
        // title. This avoids mistaking headless CLI and code-mode processes
        // for the desktop conversation window.
        return false;
    }
}
