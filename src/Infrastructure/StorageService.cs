/*
Traceability metadata
Version: 0.1.0
Generated: 2026-07-25 (Asia/Shanghai)
Last updated: 2026-07-25
Content coverage through: 2026-07-25
Source type: LOCAL-WORK
Source: Codex Desktop / workspace new-chat / current Lumi independent companion task / Codex
*/

using System.Text;
using System.Text.Json;
using LumiCompanion.Models;

namespace LumiCompanion.Infrastructure;

public sealed class StorageService
{
    public static bool IsPortable { get; } = File.Exists(Path.Combine(AppContext.BaseDirectory, "portable.flag"));
    internal static string DefaultDataDirectory { get; } = IsPortable
        ? Path.Combine(AppContext.BaseDirectory, "data")
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LumiCompanion");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public StorageService(string? rootOverride = null)
    {
        RootDirectory = rootOverride ?? DefaultDataDirectory;
        HistoryDirectory = Path.Combine(RootDirectory, "history");
        LogDirectory = Path.Combine(RootDirectory, "logs");
        Directory.CreateDirectory(RootDirectory);
        Directory.CreateDirectory(HistoryDirectory);
        Directory.CreateDirectory(LogDirectory);
    }

    public string RootDirectory { get; }
    public string HistoryDirectory { get; }
    public string LogDirectory { get; }
    public string SettingsPath => Path.Combine(RootDirectory, "config.json");
    public string StatePath => Path.Combine(RootDirectory, "state.json");

    public LumiSettings LoadSettings()
    {
        LumiSettings settings = ReadOrDefault(SettingsPath, () => new LumiSettings());
        settings.Normalize();
        SaveSettings(settings);
        return settings;
    }

    public LumiPersistentState LoadState()
    {
        LumiPersistentState state = ReadOrDefault(StatePath, () => new LumiPersistentState());
        state.Normalize();
        SaveState(state);
        return state;
    }

    public void SaveSettings(LumiSettings settings)
    {
        settings.Normalize();
        WriteJsonAtomically(SettingsPath, settings);
    }

    public void SaveState(LumiPersistentState state)
    {
        state.Normalize();
        WriteJsonAtomically(StatePath, state);
    }

    public void AppendHistory(LumiSettings settings, EventHistoryRecord record)
    {
        if (!settings.SaveEventHistory)
        {
            return;
        }

        if (!settings.StoreWindowTitles)
        {
            record.WindowTitle = null;
        }

        string path = Path.Combine(HistoryDirectory, "events-" + DateTime.Now.ToString("yyyy-MM-dd") + ".jsonl");
        if (!File.Exists(path))
        {
            var metadataRecord = new
            {
                _metadata = new TraceMetadata(),
                type = "metadata",
                privacy = "No keystrokes, scan codes, audio samples, screenshots, clipboard content, or window contents are recorded. Window titles are optional."
            };
            File.AppendAllText(path, JsonSerializer.Serialize(metadataRecord) + Environment.NewLine, new UTF8Encoding(false));
        }

        File.AppendAllText(path, JsonSerializer.Serialize(record) + Environment.NewLine, new UTF8Encoding(false));
    }

    public void WriteError(string area, Exception exception)
    {
        try
        {
            string path = Path.Combine(LogDirectory, "lumi-" + DateTime.Now.ToString("yyyy-MM-dd") + ".log");
            string line = DateTimeOffset.Now.ToString("O") + " [" + SafeToken(area) + "] " +
                          exception.GetType().Name + ": " + SanitizeError(exception.Message) + Environment.NewLine;
            File.AppendAllText(path, line, new UTF8Encoding(false));
        }
        catch
        {
            // Error logging must never crash the companion.
        }
    }

    public void DeleteHistoryAndLogs()
    {
        DeleteFilesIn(HistoryDirectory);
        DeleteFilesIn(LogDirectory);
    }

    public void PruneHistory(int retentionDays)
    {
        DateTime cutoff = DateTime.Now.Date.AddDays(-Math.Clamp(retentionDays, 1, 90));
        foreach (string directory in new[] { HistoryDirectory, LogDirectory })
        {
            if (!Directory.Exists(directory))
            {
                continue;
            }

            foreach (string file in Directory.EnumerateFiles(directory))
            {
                try
                {
                    if (File.GetLastWriteTime(file) < cutoff)
                    {
                        File.Delete(file);
                    }
                }
                catch
                {
                    // Retention is best-effort and must not interrupt the UI.
                }
            }
        }
    }

    private static T ReadOrDefault<T>(string path, Func<T> factory) where T : class
    {
        if (!File.Exists(path))
        {
            return factory();
        }

        try
        {
            string json = File.ReadAllText(path, Encoding.UTF8);
            return JsonSerializer.Deserialize<T>(json, JsonOptions) ?? factory();
        }
        catch
        {
            try
            {
                string backup = path + ".corrupt-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
                File.Move(path, backup, true);
            }
            catch
            {
                // Keep running with defaults even when backup creation fails.
            }

            return factory();
        }
    }

    private static void WriteJsonAtomically<T>(string path, T value)
    {
        string temporary = path + ".tmp";
        string json = JsonSerializer.Serialize(value, JsonOptions);
        File.WriteAllText(temporary, json, new UTF8Encoding(false));
        File.Move(temporary, path, true);
    }

    private static void DeleteFilesIn(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return;
        }

        foreach (string file in Directory.EnumerateFiles(directory))
        {
            try
            {
                File.Delete(file);
            }
            catch
            {
                // The next cleanup can retry locked files.
            }
        }
    }

    private static string SafeToken(string value)
    {
        return new string(value.Where(c => char.IsLetterOrDigit(c) || c is '-' or '_').Take(40).ToArray());
    }

    private static string SanitizeError(string value)
    {
        string compact = value.Replace('\r', ' ').Replace('\n', ' ');
        string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(userProfile))
        {
            compact = compact.Replace(userProfile, "%USERPROFILE%", StringComparison.OrdinalIgnoreCase);
        }

        return compact.Length <= 400 ? compact : compact[..400];
    }
}
