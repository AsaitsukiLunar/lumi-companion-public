// Version: 0.3.10-preview | Generated/updated/coverage: 2026-11-10 (Asia/Shanghai)
// Source: RAW | user ComfyUI dock/drag/mode request; LOCAL-WORK | Codex Desktop / Lumi / Codex
namespace LumiCompanion.Models;

public sealed record ComfyUiPanelPlacement(
    bool Docked = true, double? Left = null, double? Top = null,
    bool ShowNodeSteps = true, bool Hidden = false, string? OutputDirectory = null);
