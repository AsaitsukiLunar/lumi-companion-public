/*
Traceability metadata
Version: 1.2
Generated / Last updated / Content coverage through: 2026-11-11 (Asia/Shanghai)
Source type: RAW | user WorkBuddy integration request; LOCAL-AGENT | WorkBuddy bridge handoff; LOCAL-WORK | Lumi adapter
Organizing node: Windows / Codex Desktop / Codex / Lumi independent companion
Original sources: WorkBuddy Desktop bridge handoff V1.0 and observed local bridge files
*/

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace LumiCompanion.Integration;

public sealed record WorkBuddyTaskReadResult(AgentTaskSnapshot? Snapshot, string? ScopeKey,
    string? BridgeState, IReadOnlyList<ObservedCompletionEvent> CompletionEvents)
{
    public bool CompletionEventsAvailable { get; init; }
    public int? TurnSeq { get; init; }
    public DateTimeOffset? UpdatedAt { get; init; }
}

/// <summary>Reads the explicitly published WorkBuddy status and terminal-event files for one local workspace.</summary>
public sealed class LocalWorkBuddyTaskSource
{
    public const string SourceId = "workbuddy-bridge";
    private const string Schema = "wb-lumi-bridge/0.1";
    private const string WriterSource = "workbuddy-desktop";
    private const string BridgeDirectoryVariable = "LUMI_WORKBUDDY_BRIDGE_DIR";
    private const string StateFile = "wb-state.json";
    private const string EventFile = "wb-events.jsonl";
    private const int MaximumSnapshotBytes = 16 * 1024;
    private const int MaximumEventBytes = 4 * 1024 * 1024;
    private static readonly TimeSpan DisplayLease = TimeSpan.FromSeconds(15);
    private readonly string? _configuredDirectory;
    private readonly string? _discoveryRoot;
    private readonly TimeProvider _timeProvider;

    public LocalWorkBuddyTaskSource(string? bridgeDirectory = null, TimeProvider? timeProvider = null)
        : this(bridgeDirectory ?? Environment.GetEnvironmentVariable(BridgeDirectoryVariable), timeProvider,
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "WorkBuddy"))
    { }

    internal LocalWorkBuddyTaskSource(string? bridgeDirectory, TimeProvider? timeProvider, string discoveryRoot)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
        if (string.IsNullOrWhiteSpace(bridgeDirectory))
            _discoveryRoot = NormalizeDirectory(discoveryRoot);
        else
            _configuredDirectory = NormalizeDirectory(bridgeDirectory);
    }

    public async Task<WorkBuddyTaskReadResult> ReadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string? directory = _configuredDirectory ?? DiscoverDirectory();
        if (directory is null) return new(null, null, null, []);

        string? expectedWorkspace = StandardWorkspaceName(directory);
        byte[]? snapshotBytes = await ReadBoundedAsync(Path.Combine(directory, StateFile), MaximumSnapshotBytes, cancellationToken)
            .ConfigureAwait(false);
        byte[]? eventBytes = await ReadBoundedAsync(Path.Combine(directory, EventFile), MaximumEventBytes, cancellationToken)
            .ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        DateTimeOffset now = _timeProvider.GetUtcNow();
        var snapshot = ReadSnapshot(snapshotBytes, now, expectedWorkspace);
        string? workspace = expectedWorkspace ?? snapshot?.Workspace;
        var (eventsAvailable, events) = ReadCompletionEvents(eventBytes, now, cancellationToken, workspace);
        return new(snapshot?.Snapshot,
            snapshot is not null || eventsAvailable ? ScopeKey(directory, expectedWorkspace is null ? workspace : null) : null,
            snapshot?.BridgeState, events)
        {
            CompletionEventsAvailable = eventsAvailable,
            TurnSeq = snapshot?.TurnSeq,
            UpdatedAt = snapshot?.UpdatedAt
        };
    }

    private string? DiscoverDirectory()
    {
        if (_discoveryRoot is null) return null;
        string? found = null;
        try
        {
            var options = new EnumerationOptions { AttributesToSkip = FileAttributes.ReparsePoint };
            foreach (string workspace in Directory.EnumerateDirectories(_discoveryRoot, "*", options))
            {
                string candidate = Path.Combine(workspace, ".workbuddy", "lumi");
                if (!File.Exists(Path.Combine(candidate, StateFile)) && !File.Exists(Path.Combine(candidate, EventFile))) continue;
                if (found is not null) return null;
                found = candidate;
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return null; }
        return found;
    }

    private static string? NormalizeDirectory(string directory)
    {
        if (!Path.IsPathFullyQualified(directory)) return null;
        try { return Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory)); }
        catch (Exception error) when (error is ArgumentException or IOException or NotSupportedException) { return null; }
    }

    private static string? StandardWorkspaceName(string directory)
    {
        string? parent = Path.GetDirectoryName(directory);
        if (parent is null || !string.Equals(Path.GetFileName(directory), "lumi", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(Path.GetFileName(parent), ".workbuddy", StringComparison.OrdinalIgnoreCase)) return null;
        string? workspace = Path.GetFileName(Path.GetDirectoryName(parent));
        return IsWorkspaceName(workspace) ? workspace : null;
    }

    private static string ScopeKey(string directory, string? workspace)
    {
        string scopePath = OperatingSystem.IsWindows() ? directory.ToUpperInvariant() : directory;
        string scope = workspace is null ? scopePath : scopePath + "\n" + workspace;
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(SourceId + "\n" + scope)));
    }

    private static (AgentTaskSnapshot Snapshot, string BridgeState, int TurnSeq, DateTimeOffset UpdatedAt, string Workspace)? ReadSnapshot(
        byte[]? bytes, DateTimeOffset now, string? expectedWorkspace)
    {
        if (bytes is null) return null;
        try
        {
            using var document = JsonDocument.Parse(WithoutBom(bytes));
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !Matches(root, "schema", Schema) ||
                !Matches(root, "source", WriterSource) || ReadString(root, "workspace") is not { } workspace ||
                !IsWorkspaceName(workspace) || (expectedWorkspace is not null && workspace != expectedWorkspace) ||
                ReadTurn(root) is not { } turn || !TryReadTimestamp(root, "updatedAt", now, out var updatedAt) ||
                !TryReadInteger(root, "leaseMs", out long leaseMs) || leaseMs <= 0 ||
                leaseMs > DateTimeOffset.MaxValue.ToUnixTimeMilliseconds() - updatedAt.ToUnixTimeMilliseconds())
                return null;

            string? bridgeState = ReadString(root, "state");
            AgentTaskState state = bridgeState switch
            {
                "running" => AgentTaskState.Running,
                "waiting_user" => AgentTaskState.Waiting,
                "completed" => AgentTaskState.Completed,
                "failed" => AgentTaskState.Failed,
                "cancelled" => AgentTaskState.Idle,
                _ => AgentTaskState.Unknown
            };
            if (state == AgentTaskState.Unknown || bridgeState is null) return null;

            DateTimeOffset expiresAt = now + DisplayLease;
            if (state == AgentTaskState.Running)
            {
                var bridgeExpiry = DateTimeOffset.FromUnixTimeMilliseconds(updatedAt.ToUnixTimeMilliseconds() + leaseMs);
                if (bridgeExpiry <= now)
                {
                    state = AgentTaskState.Unknown;
                    bridgeState = "expired";
                }
                else if (bridgeExpiry < expiresAt)
                {
                    expiresAt = bridgeExpiry;
                }
            }

            return (new(SourceId, turn.TaskId, state, now, expiresAt), bridgeState, turn.Sequence, updatedAt, workspace);
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException) { return null; }
    }

    private static (bool Available, IReadOnlyList<ObservedCompletionEvent> Events) ReadCompletionEvents(
        byte[]? bytes, DateTimeOffset now, CancellationToken cancellationToken, string? workspace)
    {
        if (bytes is null || workspace is null) return (false, []);
        var completed = new Dictionary<string, ObservedCompletionEvent>(StringComparer.Ordinal);
        bool foundValidEvent = false;
        bool hasContent = false;
        var remaining = WithoutBom(bytes);
        while (!remaining.IsEmpty)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int newline = remaining.Span.IndexOf((byte)'\n');
            if (newline < 0)
            {
                hasContent |= HasContent(remaining.Span);
                break; // A writer may still be appending the final line.
            }

            var line = remaining[..newline];
            remaining = remaining[(newline + 1)..];
            if (!HasContent(line.Span)) continue;
            hasContent = true;
            try
            {
                using var document = JsonDocument.Parse(line);
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object || !Matches(root, "schema", Schema) ||
                    (root.TryGetProperty("source", out _) && !Matches(root, "source", WriterSource)) ||
                    (root.TryGetProperty("workspace", out _) && !Matches(root, "workspace", workspace)) ||
                    ReadTurn(root) is not { } turn || !TryReadTimestamp(root, "at", now, out var eventAt))
                    continue;

                string? state = ReadString(root, "state");
                if (state is not ("completed" or "failed" or "cancelled")) continue;
                foundValidEvent = true;
                if (state == "completed" &&
                    (!completed.TryGetValue(turn.TaskId, out var previous) || eventAt < previous.EventAt))
                    completed[turn.TaskId] = new(SourceId, turn.TaskId, turn.TurnId, "turn:" + turn.TurnId, eventAt);
            }
            catch (Exception error) when (error is JsonException or InvalidOperationException)
            { /* Invalid JSON or string encoding does not invalidate the other complete records. */ }
        }
        return (foundValidEvent || !hasContent, completed.Values.ToArray());
    }

    private static TurnIdentity? ReadTurn(JsonElement root)
    {
        string? session = ReadString(root, "sessionId");
        string? turn = ReadString(root, "turnId");
        if (!IsIdentifier(session) || !IsIdentifier(turn) ||
            !TryReadInteger(root, "turnSeq", out long sequence) || sequence is <= 0 or > int.MaxValue)
            return null;
        return new(session + "/" + turn, turn!, (int)sequence);
    }

    private static bool IsIdentifier(string? value) => value is { Length: > 0 and <= 128 } &&
        value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.' or ':');

    private static bool IsWorkspaceName(string? value) => value is { Length: > 0 and <= 255 } &&
        value is not ("." or "..") && !string.IsNullOrWhiteSpace(value) &&
        value.All(c => !char.IsControl(c) && !"\\/:*?\"<>|".Contains(c));

    private static string? ReadString(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() : null;

    private static bool Matches(JsonElement root, string name, string expected) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String && value.ValueEquals(expected);

    private static bool TryReadInteger(JsonElement root, string name, out long number)
    {
        number = 0;
        return root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number &&
            value.TryGetInt64(out number);
    }

    private static bool TryReadTimestamp(JsonElement root, string name, DateTimeOffset now, out DateTimeOffset timestamp)
    {
        timestamp = default;
        if (!TryReadInteger(root, name, out long epochMs) || epochMs <= 0 || epochMs > now.ToUnixTimeMilliseconds())
            return false;
        timestamp = DateTimeOffset.FromUnixTimeMilliseconds(epochMs);
        return true;
    }

    private static bool HasContent(ReadOnlySpan<byte> bytes)
    {
        foreach (byte value in bytes)
            if (value is not ((byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n')) return true;
        return false;
    }

    private static ReadOnlyMemory<byte> WithoutBom(byte[] bytes) =>
        bytes.AsSpan() is [0xef, 0xbb, 0xbf, ..] ? bytes.AsMemory(3) : bytes;

    private static async Task<byte[]?> ReadBoundedAsync(string path, int limit, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete,
                bufferSize: 4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
            long length = stream.Length;
            if (length > limit) return null;
            byte[] bytes = new byte[(int)length];
            int read = await stream.ReadAtLeastAsync(bytes, bytes.Length, false, cancellationToken)
                .ConfigureAwait(false);
            return read == bytes.Length ? bytes : bytes[..read];
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return null; }
    }

    private readonly record struct TurnIdentity(string TaskId, string TurnId, int Sequence);
}
