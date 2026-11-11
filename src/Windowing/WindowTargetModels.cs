/*
Traceability metadata
Version: 0.3.0-dev
Generated: 2026-08-09 (Asia/Shanghai)
Last updated: 2026-08-09
Content coverage through: 2026-08-09
Source type: LOCAL-WORK
Source: Codex Desktop / workspace new-chat / current Lumi independent companion task / Codex
*/

namespace LumiCompanion.Windowing;

public readonly record struct PixelRect(int Left, int Top, int Right, int Bottom)
{
    public int Width => Math.Max(0, Right - Left);
    public int Height => Math.Max(0, Bottom - Top);
    public bool IsUsable => Width >= 160 && Height >= 120;
}

public sealed record TargetWindowSnapshot
{
    public required IntPtr Handle { get; init; }
    public required string ProcessName { get; init; }
    public string Title { get; init; } = string.Empty;
    public string ExecutablePath { get; init; } = string.Empty;
    public string PackageFullName { get; init; } = string.Empty;
    public required PixelRect Bounds { get; init; }
    public bool IsFullscreen { get; init; }
    public bool IsMinimized { get; init; }
    public DateTimeOffset ObservedAt { get; init; } = DateTimeOffset.Now;
}
