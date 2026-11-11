// Version: 0.3.7-preview; Generated/Updated/Coverage: 2026-10-30 (Asia/Shanghai)
// Source: RAW | user automatic cross-restart completion counts; LOCAL-WORK | completion_ledger / Windows / Codex Desktop.
// Original sources: existing verified account-scoped local turn observations. No conversation body or credentials.
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LumiCompanion.Integration;
using LumiCompanion.Models;

namespace LumiCompanion.Infrastructure;

/// <summary>Append-only, account-isolated counts of newly observed technical turn completions.</summary>
public sealed class CompletionHistoryService
{
    private readonly TimeProvider _time;
    private readonly TimeZoneInfo _zone;
    private readonly List<CompletionHistoryEntry> _entries = [];
    private readonly HashSet<string> _keys = new(StringComparer.Ordinal);
    private readonly HashSet<string> _thisLaunch = new(StringComparer.Ordinal);
    private DateTimeOffset _recordFrom;
    private string? _scopeHash;
    private string? _source;
    private bool _sourceAvailable, _enabled, _paused;
    private bool _ledgerExisted;
    private long _expectedLength;
    private DateTime _expectedWriteTime;
    private string? _problem;
    public string FilePath { get; }

    public CompletionHistoryService(string rootDirectory, bool enabled, TimeProvider? timeProvider = null,
        TimeZoneInfo? timeZone = null)
    {
        FilePath = Path.Combine(rootDirectory, "completion-history.jsonl");
        _time = timeProvider ?? TimeProvider.System;
        _zone = timeZone ?? TimeZoneInfo.Local;
        _recordFrom = _time.GetUtcNow();
        _enabled = enabled;
        Load();
    }

    public void SetEnabled(bool enabled)
    {
        if (enabled && !_enabled) _recordFrom = _time.GetUtcNow();
        _enabled = enabled;
    }

    public void SetSourceUnavailable() => _sourceAvailable = false;

    public void SetObservationPaused(bool paused)
    {
        if (_paused && !paused) _recordFrom = _time.GetUtcNow();
        _paused = paused;
        if (paused) _sourceAvailable = false;
    }

    public void Apply(LocalCodexTaskReadResult result)
    {
        _sourceAvailable = result.SourceAvailable && !string.IsNullOrWhiteSpace(result.AccountScopeKey);
        if (!_sourceAvailable) return;
        ApplyVerified(LocalCodexTaskSource.SourceId, result.AccountScopeKey!, result.CompletionEvents);
    }

    /// <summary>In-process entry for an already verified source adapter; not a network/file ingestion endpoint.</summary>
    public void ApplyVerified(string source, string accountScope, IReadOnlyList<ObservedCompletionEvent> completions)
    {
        if (!IsSource(source) || string.IsNullOrWhiteSpace(accountScope)) { SetSourceUnavailable(); return; }
        _sourceAvailable = !_paused;
        _source = source;
        _scopeHash = Hash([source, accountScope]);
        if (!_enabled || _paused || _problem is not null) return;
        DateTimeOffset now = _time.GetUtcNow();
        var additions = new List<CompletionHistoryEntry>();
        var pending = new HashSet<string>(StringComparer.Ordinal);
        foreach (ObservedCompletionEvent observation in completions)
        {
            if (observation.Source != source || string.IsNullOrWhiteSpace(observation.TaskId) ||
                string.IsNullOrWhiteSpace(observation.TurnId) || string.IsNullOrWhiteSpace(observation.StateVersion) ||
                observation.EventAt < _recordFrom || observation.EventAt > now) continue;
            string key = Hash([observation.Source, _scopeHash, observation.TaskId, observation.TurnId]);
            if (_keys.Contains(key) || !pending.Add(key)) continue;
            additions.Add(new(_scopeHash, key, source, observation.EventAt.ToUniversalTime()));
        }
        if (additions.Count == 0) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            // Never recreate a ledger that was removed or replaced after startup.
            // A fresh missing file is created exclusively; an existing ledger stays append-only.
            bool create = !File.Exists(FilePath);
            if (create && _ledgerExisted) throw new InvalidDataException("Completion ledger disappeared.");
            if (!create && (!_ledgerExisted || File.GetLastWriteTimeUtc(FilePath) != _expectedWriteTime))
                throw new InvalidDataException("Completion ledger changed outside this process.");
            long writtenLength;
            using (var stream = new FileStream(FilePath, create ? FileMode.CreateNew : FileMode.Append,
                FileAccess.Write, FileShare.Read))
            {
                if (!create && stream.Length != _expectedLength) throw new InvalidDataException("Completion ledger length changed.");
                using (var writer = new StreamWriter(stream, new UTF8Encoding(false), leaveOpen: true))
                {
                    if (create)
                        writer.WriteLine(JsonSerializer.Serialize(new
                        {
                            type = "metadata", schemaVersion = 1, _metadata = new TraceMetadata(),
                            meaning = "Observed technical turn completions while Lumi is running; not business acceptance.",
                            fields = "scope/event hashes, source and original event timestamp; no conversation content"
                        }));
                    foreach (CompletionHistoryEntry entry in additions) writer.WriteLine(JsonSerializer.Serialize(entry));
                }
                stream.Flush(flushToDisk: true);
                writtenLength = stream.Length;
            }
            _ledgerExisted = true;
            _expectedLength = writtenLength;
            _expectedWriteTime = File.GetLastWriteTimeUtc(FilePath);
            foreach (CompletionHistoryEntry entry in additions)
            {
                _entries.Add(entry); _keys.Add(entry.EventKey); _thisLaunch.Add(entry.EventKey);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        { _problem = "完成记录未能保存；原文件保留，统计暂不可用。"; }
    }

    public CompletionHistorySnapshot Read()
    {
        if (_problem is not null) return new(_enabled, false, _problem, null, null, null, []);
        if (!_sourceAvailable || _scopeHash is null)
            return new(_enabled, false, "当前对话来源不可用；历史记录已保留。", null, null, null, []);
        DateTime today = TimeZoneInfo.ConvertTime(_time.GetUtcNow(), _zone).Date;
        CompletionHistoryEntry[] scoped = _entries.Where(entry => entry.ScopeHash == _scopeHash).ToArray();
        return new(_enabled, true, _enabled
                ? "统计当前账号在 Lumi 运行期间观测到的已结束回合；不等于作品验收。"
                : "本地统计已关闭；历史仍可查看，重新启用后记录新回合。",
            scoped.Count(entry => _thisLaunch.Contains(entry.EventKey)),
            scoped.Count(entry => TimeZoneInfo.ConvertTime(entry.EventAt, _zone).Date == today), scoped.Length,
            scoped.OrderByDescending(entry => entry.EventAt).Take(10).ToArray(), _source);
    }

    private void Load()
    {
        if (!File.Exists(FilePath)) return;
        _ledgerExisted = true;
        try
        {
            using var ledger = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (ledger.Length == 0) throw new InvalidDataException();
            ledger.Seek(-1, SeekOrigin.End);
            if (ledger.ReadByte() != '\n') throw new InvalidDataException("Incomplete final ledger record.");
            ledger.Position = 0;
            using var reader = new StreamReader(ledger, Encoding.UTF8);
            string? header = reader.ReadLine();
            if (header is null) throw new InvalidDataException();
            using (JsonDocument json = JsonDocument.Parse(header))
                if (json.RootElement.GetProperty("type").GetString() != "metadata" ||
                    json.RootElement.GetProperty("schemaVersion").GetInt32() != 1)
                    throw new InvalidDataException();
            string? line;
            while ((line = reader.ReadLine()) is not null)
            {
                CompletionHistoryEntry? entry = JsonSerializer.Deserialize<CompletionHistoryEntry>(line);
                if (entry is null || !IsHash(entry.ScopeHash) || !IsHash(entry.EventKey) ||
                    !IsSource(entry.Source) || entry.EventAt == default)
                    throw new InvalidDataException();
                if (_keys.Add(entry.EventKey)) _entries.Add(entry);
            }
            _expectedLength = reader.BaseStream.Length;
            _expectedWriteTime = File.GetLastWriteTimeUtc(FilePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or
            InvalidDataException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            _entries.Clear(); _keys.Clear();
            _problem = "完成记录暂不可读；原文件保留，不会用零值覆盖。";
        }
    }

    private static bool IsHash(string? value) => value is { Length: 64 } && value.All(Uri.IsHexDigit);
    private static bool IsSource(string? value) => value is { Length: > 0 and <= 64 } &&
        value.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.');
    private static string Hash(string[] values) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(values))));
}
