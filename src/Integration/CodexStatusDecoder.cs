// Version: 0.3.0 | Generated/Updated/Coverage: 2026-10-08
// Source: LOCAL-WORK / Codex Desktop / current Lumi conversation / Codex
using System.Text.Json;
namespace LumiCompanion.Integration;

/// <summary>Decodes official status notifications, not conversation bodies.
/// A verified connection to the originating server is still required.</summary>
public static class CodexStatusDecoder
{
    public static AgentTaskSnapshot? Decode(JsonElement message, DateTimeOffset now, TimeSpan lease)
    {
        if (lease <= TimeSpan.Zero || message.ValueKind != JsonValueKind.Object ||
            !message.TryGetProperty("method", out var method) || method.ValueKind != JsonValueKind.String ||
            method.GetString() != "thread/status/changed" ||
            !message.TryGetProperty("params", out var p) || p.ValueKind != JsonValueKind.Object ||
            !p.TryGetProperty("threadId", out var id) || id.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(id.GetString()) ||
            !p.TryGetProperty("status", out var status) || status.ValueKind != JsonValueKind.Object ||
            !status.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String)
            return null;
        var state = type.GetString() switch
        {
            "active" => AgentTaskState.Running, "idle" => AgentTaskState.Idle,
            "systemError" => AgentTaskState.Failed, _ => AgentTaskState.Unknown
        };
        if (state == AgentTaskState.Running && status.TryGetProperty("activeFlags", out var flags) &&
            flags.ValueKind == JsonValueKind.Array && flags.EnumerateArray().Any(f =>
                f.ValueKind == JsonValueKind.String && f.GetString() is "waitingOnApproval" or "waitingOnUserInput"))
            state = AgentTaskState.Waiting;
        return new("codex", id.GetString()!, state, now, now.Add(lease));
    }
}
