/*
Version: 0.3.8-preview
Generated: 2026-10-30 (Asia/Shanghai)
Last updated / Content coverage through: 2026-11-10 (Asia/Shanghai)
Source type: RAW | current user task-link request; LOCAL-WORK | local schema observation and implementation
Organizing node: Windows / Codex Desktop / Codex / github_reuse subtask
Original sources: account-filtered lifecycle metadata, including the 2026-11-10 same-turn start outside the normal tail.
No credentials, conversation text, network requests, app-server or job startup are used.
*/
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace LumiCompanion.Integration;

public sealed record LocalCodexTaskObservation(
    AgentTaskSnapshot Snapshot, string StateVersion, DateTimeOffset EventAt, string EventType)
{
    public string? TurnId { get; init; }
}

public sealed record LocalCodexTaskReadResult(
    IReadOnlyList<LocalCodexTaskObservation> Observations, bool SourceAvailable,
    string? AccountScopeKey, string StatusMessage, int InspectedFiles, int MatchedFiles)
{
    // Separate accounting evidence from the latest per-chat animation state.
    public IReadOnlyList<ObservedCompletionEvent> CompletionEvents { get; init; } = [];
}

public sealed record ObservedCompletionEvent(string Source, string TaskId, string TurnId,
    string StateVersion, DateTimeOffset EventAt);

/// <summary>
/// Observes bounded tails of existing, current-account desktop chats. Completed means a turn ended,
/// not that the user's business objective or visual acceptance was achieved. Waiting/review are not inferred.
/// The 32 most recently modified files are candidates, not a guarantee of all desktop-chat coverage.
/// </summary>
public sealed class LocalCodexTaskSource
{
    public const string SourceId = "codex-local-verified";
    public const int MaximumFiles = 32;
    public const int TailBytes = 256 * 1024;
    private const int RecoveryTailBytes = 4 * 1024 * 1024;
    private const int MaximumHeaderBytes = 1024 * 1024;
    public static readonly TimeSpan ActivityFreshness = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan ObservationHistory = TimeSpan.FromHours(24);
    private readonly string _codexRoot;
    private readonly TimeProvider _timeProvider;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<string, CachedSession> _cache = new(StringComparer.OrdinalIgnoreCase);
    private string? _scope;

    public LocalCodexTaskSource(string? codexRoot = null, TimeProvider? timeProvider = null)
    {
        _codexRoot = codexRoot ?? Environment.GetEnvironmentVariable("CODEX_HOME") ??
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex");
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<LocalCodexTaskReadResult> ReadAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { return await Task.Run(() => Read(cancellationToken), cancellationToken).ConfigureAwait(false); }
        finally { _gate.Release(); }
    }

    private LocalCodexTaskReadResult Read(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        DateTimeOffset now = _timeProvider.GetUtcNow();
        string accountId, accountUserId;
        try
        {
            using var stream = OpenRead(Path.Combine(_codexRoot, ".codex-global-state.json"));
            if (stream.Length > 8 * 1024 * 1024) return Unavailable();
            using var json = JsonDocument.Parse(stream);
            if (!Object(json.RootElement, "electron-persisted-atom-state", out var atom) ||
                !Object(atom, "local-document-identity", out var identity) ||
                !String(identity, "accountId", out accountId) ||
                !String(identity, "accountUserId", out accountUserId)) return Unavailable();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        { return Unavailable(); }

        string scope = Hash(accountUserId);
        if (_scope != scope) { _cache.Clear(); _scope = scope; }
        var observations = new List<LocalCodexTaskObservation>();
        var completions = new List<ObservedCompletionEvent>();
        string sessionsRoot = Path.Combine(_codexRoot, "sessions");
        if (!Directory.Exists(sessionsRoot))
        {
            _cache.Clear();
            return new(observations, false, scope, "本地会话目录不可用；任务来源未知", 0, 0);
        }
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true, IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint
        };
        FileInfo[] candidates;
        try
        {
            candidates = new DirectoryInfo(sessionsRoot).EnumerateFiles("*.jsonl", options)
                .OrderByDescending(file => file.LastWriteTimeUtc).Take(MaximumFiles).ToArray();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _cache.Clear();
            return new(observations, false, scope, "当前账号的本地会话目录暂不可读；任务来源未知", 0, 0);
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int matched = 0;
        foreach (FileInfo file in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            seen.Add(file.FullName);
            try
            {
                long length = file.Length;
                DateTime modified = file.LastWriteTimeUtc;
                if (!_cache.TryGetValue(file.FullName, out var cached) || cached.Length != length ||
                    cached.ModifiedAt != modified)
                {
                    cached = ReadSession(file, accountId, accountUserId, now, cancellationToken);
                    _cache[file.FullName] = cached;
                }
                if (!cached.Matched) continue;
                matched++;
                foreach (ParsedEvent completion in cached.Completions)
                    if (completion.EventAt <= now.AddMinutes(1) && now - completion.EventAt <= ObservationHistory)
                        completions.Add(new(SourceId, cached.ThreadId!, completion.TurnId, completion.Version, completion.EventAt));
                if (cached.Event is not { } observed) continue;
                if (observed.EventAt > now.AddMinutes(1) || now - observed.EventAt > ObservationHistory) continue;
                AgentTaskState state = observed.State;
                if (state == AgentTaskState.Running && now - observed.LastActivityAt > ActivityFreshness)
                    state = AgentTaskState.Unknown;
                // Read time renews a bounded display lease; it is never the event/version identity.
                var snapshot = new AgentTaskSnapshot(SourceId, cached.ThreadId!, state, now, now.AddSeconds(15));
                observations.Add(new(snapshot, observed.Version, observed.EventAt, observed.EventType)
                { TurnId = observed.TurnId });
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            { _cache.Remove(file.FullName); }
        }
        foreach (string path in _cache.Keys.Where(path => !seen.Contains(path)).ToArray()) _cache.Remove(path);
        return new(observations, true, scope,
            "本地回合观测；正常结束不等于用户验收；等待与审阅未推断", candidates.Length, matched)
        { CompletionEvents = completions };
    }

    private LocalCodexTaskReadResult Unavailable()
    {
        _cache.Clear(); _scope = null;
        return new(Array.Empty<LocalCodexTaskObservation>(), false, null,
            "桌面账号标识不可用；本地任务来源未知", 0, 0);
    }

    private static CachedSession ReadSession(FileInfo file, string accountId, string accountUserId,
        DateTimeOffset now, CancellationToken cancellationToken)
    {
        using var stream = OpenRead(file.FullName);
        long length = stream.Length;
        var missing = new CachedSession(length, file.LastWriteTimeUtc, false, null, null);
        string? header = FirstLine(stream, cancellationToken);
        if (header is null) return missing;
        using var metadata = JsonDocument.Parse(header);
        if (!String(metadata.RootElement, "type", out string metadataType) || metadataType != "session_meta" ||
            !Object(metadata.RootElement, "payload", out var payload) ||
            !String(payload, "id", out string threadId) ||
            !String(payload, "creator_account_id", out string creatorAccount) || creatorAccount != accountId ||
            !String(payload, "creator_user_id", out string creatorUser) ||
            accountUserId != creatorUser + "__" + creatorAccount ||
            !String(payload, "originator", out string originator) ||
            originator is not ("Codex Desktop" or "codex_work_desktop") ||
            // Only the desktop main-chat source actually observed locally is enabled.
            !String(payload, "source", out string source) || source != "vscode") return missing;

        ParsedEvent? latest = null;
        var terminals = new Dictionary<string, ParsedEvent>(StringComparer.Ordinal);
        foreach (string line in ReadTailLines(stream, length, TailBytes, cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                using var document = JsonDocument.Parse(line);
                latest = ParseEvent(document.RootElement, latest, threadId, now);
                ParsedEvent? terminal = ParseEvent(document.RootElement, null, threadId, now);
                if (terminal is { EventType: "task_complete" or "turn_aborted" } &&
                    (!terminals.TryGetValue(terminal.TurnId, out ParsedEvent? old) || terminal.EventAt >= old.EventAt))
                    terminals[terminal.TurnId] = terminal;
            }
            catch (JsonException) { /* Partial/concurrently appended records are not state. */ }
        }

        if (latest is { EventType: "unmatched_item_activity" } unmatched)
        {
            terminals.TryGetValue(unmatched.TurnId, out var terminal);
            ParsedEvent? recovered = length > TailBytes
                ? RecoverUnmatchedTurn(stream, length, threadId, unmatched, terminal, now, cancellationToken)
                : terminal;
            if (recovered is not null)
            {
                latest = recovered;
                if (recovered.EventType is "task_complete" or "turn_aborted")
                    terminals[recovered.TurnId] = recovered;
            }
        }

        return new(length, file.LastWriteTimeUtc, true, threadId, latest)
        { Completions = terminals.Values.Where(terminal => terminal.State == AgentTaskState.Completed).ToArray() };
    }

    private static ParsedEvent? RecoverUnmatchedTurn(FileStream stream, long length, string threadId,
        ParsedEvent unmatched, ParsedEvent? terminal, DateTimeOffset now, CancellationToken cancellationToken)
    {
        ParsedEvent? start = null;
        // Only this turn's lifecycle can resolve the uncertainty. Older neighboring turns
        // in the wider window must not change this chat's current state or completion count.
        foreach (string line in ReadTailLines(stream, length, RecoveryTailBytes, cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                using var document = JsonDocument.Parse(line);
                ParsedEvent? evidence = ParseEvent(document.RootElement, null, threadId, now);
                if (evidence is null || evidence.TurnId != unmatched.TurnId) continue;
                if (evidence.EventType == "task_started" && evidence.EventAt <= unmatched.LastActivityAt &&
                    (start is null || evidence.EventAt >= start.EventAt))
                    start = evidence;
                else if (evidence.EventType is "task_complete" or "turn_aborted" &&
                    (terminal is null || evidence.EventAt >= terminal.EventAt))
                    terminal = evidence;
            }
            catch (JsonException) { /* Only complete, valid records are evidence. */ }
        }

        if (terminal is not null && (start is null || terminal.EventAt >= start.EventAt))
            return terminal;
        return start is null ? null : start with { LastActivityAt = unmatched.LastActivityAt };
    }

    private static string[] ReadTailLines(FileStream stream, long length, int limit, CancellationToken cancellationToken)
    {
        long offset = Math.Max(0, length - limit);
        stream.Seek(offset, SeekOrigin.Begin);
        byte[] buffer = new byte[(int)(length - offset)];
        int read = 0;
        while (read < buffer.Length)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int count = stream.Read(buffer, read, buffer.Length - read);
            if (count == 0) break;
            read += count;
        }
        string tail = Encoding.UTF8.GetString(buffer, 0, read);
        int start = offset > 0 ? tail.IndexOf('\n') + 1 : 0;
        int end = tail.LastIndexOf('\n');
        return end >= start && (offset == 0 || start > 0) ? tail[start..end].Split('\n') : [];
    }

    private static ParsedEvent? ParseEvent(JsonElement root, ParsedEvent? previous, string threadId, DateTimeOffset now)
    {
        if (!String(root, "type", out string recordType) || recordType != "event_msg" ||
            !String(root, "timestamp", out string timestamp) ||
            !DateTimeOffset.TryParse(timestamp, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var at) ||
            at > now.AddMinutes(1) || now - at > ObservationHistory ||
            !Object(root, "payload", out var payload) || !String(payload, "type", out string type) ||
            !String(payload, "turn_id", out string turnId) ||
            (previous is not null && at < previous.EventAt)) return previous;

        if (type == "item_completed")
        {
            if (previous is not null && previous.TurnId == turnId)
                return (previous.State == AgentTaskState.Running || previous.EventType == "unmatched_item_activity") &&
                    at > previous.LastActivityAt ? previous with { LastActivityAt = at } : previous;
            // A tail can begin after task_started. Activity alone proves neither Running nor Completed.
            return New(AgentTaskState.Unknown, "unmatched_item_activity");
        }
        if (type is not ("task_started" or "task_complete" or "turn_aborted")) return previous;
        AgentTaskState state = type switch
        {
            "task_started" => AgentTaskState.Running,
            "turn_aborted" => AgentTaskState.Idle,
            _ => AgentTaskState.Completed
        };
        if (type == "task_complete" && payload.TryGetProperty("error", out var error) && error.ValueKind != JsonValueKind.Null)
        {
            // Empty/unknown error shapes do not establish a failure or a successful ending.
            state = IsExplicitError(error) ? AgentTaskState.Failed : AgentTaskState.Unknown;
        }
        return New(state, type);

        ParsedEvent New(AgentTaskState value, string eventType) => new(value, turnId, at, at, eventType,
            Hash(threadId + "\n" + turnId + "\n" + eventType + "\n" + at.ToUniversalTime().ToString("O")));
    }

    private static bool IsExplicitError(JsonElement error)
    {
        if (error.ValueKind == JsonValueKind.String) return !string.IsNullOrWhiteSpace(error.GetString());
        if (error.ValueKind != JsonValueKind.Object) return false;
        if (String(error, "message", out _)) return true;
        if (!error.TryGetProperty("codex_error_info", out var info)) return false;
        return info.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(info.GetString()) ||
               info.ValueKind == JsonValueKind.Object && String(info, "type", out _);
    }

    private static string? FirstLine(FileStream stream, CancellationToken cancellationToken)
    {
        using var bytes = new MemoryStream();
        for (int i = 0; i < MaximumHeaderBytes; i++)
        {
            if ((i & 4095) == 0) cancellationToken.ThrowIfCancellationRequested();
            int b = stream.ReadByte();
            if (b < 0 || b == '\n') return Encoding.UTF8.GetString(bytes.ToArray()).TrimStart('\uFEFF');
            bytes.WriteByte((byte)b);
        }
        return null;
    }

    private static FileStream OpenRead(string path) => new(path, FileMode.Open, FileAccess.Read,
        FileShare.ReadWrite | FileShare.Delete);
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static bool String(JsonElement value, string name, out string text)
    {
        text = string.Empty;
        if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty(name, out var item) ||
            item.ValueKind != JsonValueKind.String) return false;
        text = item.GetString() ?? string.Empty;
        return !string.IsNullOrWhiteSpace(text) && text.Length <= 512;
    }
    private static bool Object(JsonElement value, string name, out JsonElement result)
    {
        result = default;
        return value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out result) &&
               result.ValueKind == JsonValueKind.Object;
    }
    private sealed record ParsedEvent(AgentTaskState State, string TurnId, DateTimeOffset EventAt,
        DateTimeOffset LastActivityAt, string EventType, string Version);
    private sealed record CachedSession(long Length, DateTime ModifiedAt, bool Matched, string? ThreadId, ParsedEvent? Event)
    {
        public IReadOnlyList<ParsedEvent> Completions { get; init; } = [];
    }
}
