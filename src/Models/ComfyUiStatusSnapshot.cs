/*
Traceability metadata
Version: 0.3.9-preview
Generated: 2026-11-10 (Asia/Shanghai)
Last updated: 2026-11-10
Content coverage through: 2026-11-10
Source type: RAW | user request for local ComfyUI status; LOCAL-WORK | snapshot model
Source: Codex Desktop / lumi-companion / independent Lumi companion chat / Codex
Organizing node: Windows / Codex Desktop / ComfyUI HTTP status subtask
Original sources: local ComfyUI queue and recent execution history requirements.
*/

namespace LumiCompanion.Models;

public sealed record ComfyUiStatusSnapshot(
    DateTimeOffset CapturedAt,
    int? Pending,
    int? Running,
    IReadOnlyList<string> RunningPromptIds,
    int? Succeeded,
    int? Failed,
    int? SavedOutputs,
    string Status);
