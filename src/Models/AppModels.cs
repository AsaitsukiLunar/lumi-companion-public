/*
Traceability metadata
Version: 0.3.14-beta.1
Generated: 2026-07-25 (Asia/Shanghai)
Last updated: 2026-11-11
Content coverage through: 2026-11-11
Source type: LOCAL-WORK; RAW (user quota reset time and ComfyUI panel placement requests)
Source: Codex Desktop / workspace new-chat / current Lumi independent companion task / Codex
*/

using System.Text.Json.Serialization;

namespace LumiCompanion.Models;

public static class AppIdentity
{
    public const string Version = "0.3.14-beta.1";
    public const string ProductName = "Lumi for Codex · 开发预览";
    public const string SourceType = "LOCAL-WORK";
    public const string Source = "Codex Desktop / workspace new-chat / current Lumi independent companion task / Codex";
}

public sealed class TraceMetadata
{
    [JsonPropertyName("version")]
    public string Version { get; set; } = AppIdentity.Version;

    [JsonPropertyName("generatedAt")]
    public string GeneratedAt { get; set; } = DateTimeOffset.Now.ToString("O");

    [JsonPropertyName("lastUpdatedAt")]
    public string LastUpdatedAt { get; set; } = DateTimeOffset.Now.ToString("O");

    [JsonPropertyName("contentCoverageThrough")]
    public string ContentCoverageThrough { get; set; } = DateTimeOffset.Now.ToString("yyyy-MM-dd");

    [JsonPropertyName("sourceType")]
    public string SourceType { get; set; } = AppIdentity.SourceType;

    [JsonPropertyName("source")]
    public string Source { get; set; } = AppIdentity.Source;

    public void Touch()
    {
        Version = AppIdentity.Version;
        LastUpdatedAt = DateTimeOffset.Now.ToString("O");
        ContentCoverageThrough = DateTimeOffset.Now.ToString("yyyy-MM-dd");
    }
}

public sealed class LumiSettings
{
    [JsonPropertyName("_metadata")]
    public TraceMetadata Metadata { get; set; } = new();

    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; set; } = 3;

    [JsonPropertyName("petWidth")]
    public int PetWidth { get; set; } = 192;

    [JsonPropertyName("petWidthUnit")]
    public string PetWidthUnit { get; set; } = "physical-pixel";

    [JsonPropertyName("positionX")]
    public double? PositionX { get; set; }

    [JsonPropertyName("positionY")]
    public double? PositionY { get; set; }

    [JsonPropertyName("alwaysOnTop")]
    public bool AlwaysOnTop { get; set; } = true;

    [JsonPropertyName("clickThrough")]
    public bool ClickThrough { get; set; }

    [JsonPropertyName("startWithWindows")]
    public bool StartWithWindows { get; set; }

    [JsonPropertyName("sensorsPaused")]
    public bool SensorsPaused { get; set; }

    [JsonPropertyName("monitorInputActivity")]
    public bool MonitorInputActivity { get; set; } = true;

    [JsonPropertyName("distinguishKeyboardAndMouse")]
    public bool DistinguishKeyboardAndMouse { get; set; } = true;

    [JsonPropertyName("monitorWindows")]
    public bool MonitorWindows { get; set; } = true;

    [JsonPropertyName("readWindowTitles")]
    public bool ReadWindowTitles { get; set; } = true;

    [JsonPropertyName("monitorAudio")]
    public bool MonitorAudio { get; set; } = true;

    [JsonPropertyName("mouseLook")]
    public bool MouseLook { get; set; } = true;

    [JsonPropertyName("hideInFullscreen")]
    public bool HideInFullscreen { get; set; }

    [JsonPropertyName("showCodexUsage")]
    public bool ShowCodexUsage { get; set; }

    [JsonPropertyName("showCodexUsageResetTimes")]
    public bool ShowCodexUsageResetTimes { get; set; }

    [JsonPropertyName("codexUsageRefreshMinutes")]
    public int CodexUsageRefreshMinutes { get; set; } = 5;

    [JsonPropertyName("saveLocalStats")]
    public bool SaveLocalStats { get; set; } = true;

    [JsonPropertyName("saveEventHistory")]
    public bool SaveEventHistory { get; set; }

    [JsonPropertyName("storeWindowTitles")]
    public bool StoreWindowTitles { get; set; }

    [JsonPropertyName("historyRetentionDays")]
    public int HistoryRetentionDays { get; set; } = 7;

    [JsonPropertyName("drowsyAfterSeconds")]
    public int DrowsyAfterSeconds { get; set; } = 120;

    [JsonPropertyName("sleepAfterSeconds")]
    public int SleepAfterSeconds { get; set; } = 600;

    [JsonPropertyName("aiEnabled")]
    public bool AiEnabled { get; set; }

    [JsonPropertyName("voiceEnabled")]
    public bool VoiceEnabled { get; set; }

    public LumiSettings Clone()
    {
        return (LumiSettings)MemberwiseClone();
    }

    public void Normalize()
    {
        SchemaVersion = 3;
        PetWidth = Math.Clamp(PetWidth, 96, 320);
        PetWidthUnit = "physical-pixel";
        CodexUsageRefreshMinutes = Math.Clamp(CodexUsageRefreshMinutes, 1, 60);
        HistoryRetentionDays = Math.Clamp(HistoryRetentionDays, 1, 90);
        DrowsyAfterSeconds = Math.Clamp(DrowsyAfterSeconds, 30, 3600);
        SleepAfterSeconds = Math.Clamp(SleepAfterSeconds, DrowsyAfterSeconds + 30, 14400);
        if (!SaveEventHistory)
        {
            StoreWindowTitles = false;
        }

        // Optional network modules are intentionally not present in v0.2.0.
        AiEnabled = false;
        VoiceEnabled = false;
        Metadata ??= new TraceMetadata();
        Metadata.Touch();
    }
}

public sealed class LumiPersistentState
{
    [JsonPropertyName("_metadata")]
    public TraceMetadata Metadata { get; set; } = new();

    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; set; } = 1;

    [JsonPropertyName("mood")]
    public int Mood { get; set; } = 70;

    [JsonPropertyName("energy")]
    public int Energy { get; set; } = 80;

    [JsonPropertyName("affection")]
    public int Affection { get; set; }

    [JsonPropertyName("companionMinutes")]
    public long CompanionMinutes { get; set; }

    [JsonPropertyName("activeMinutes")]
    public long ActiveMinutes { get; set; }

    [JsonPropertyName("interactionCount")]
    public long InteractionCount { get; set; }

    [JsonPropertyName("windowSwitchCount")]
    public long WindowSwitchCount { get; set; }

    [JsonPropertyName("windowOpenCount")]
    public long WindowOpenCount { get; set; }

    [JsonPropertyName("audioStartCount")]
    public long AudioStartCount { get; set; }

    [JsonPropertyName("keyboardActivityBuckets")]
    public long KeyboardActivityBuckets { get; set; }

    [JsonPropertyName("mouseActivityBuckets")]
    public long MouseActivityBuckets { get; set; }

    [JsonPropertyName("comfyUiPanel")]
    public ComfyUiPanelPlacement ComfyUiPanel { get; set; } = new();

    [JsonPropertyName("lastRunAt")]
    public string LastRunAt { get; set; } = DateTimeOffset.Now.ToString("O");

    public void Normalize()
    {
        Mood = Math.Clamp(Mood, 0, 100);
        Energy = Math.Clamp(Energy, 0, 100);
        Affection = Math.Max(0, Affection);
        ComfyUiPanel ??= new();
        Metadata ??= new TraceMetadata();
        Metadata.Touch();
        LastRunAt = DateTimeOffset.Now.ToString("O");
    }
}

public sealed class EventHistoryRecord
{
    [JsonPropertyName("timestamp")]
    public string Timestamp { get; set; } = DateTimeOffset.Now.ToString("O");

    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("category")]
    public string? Category { get; set; }

    [JsonPropertyName("process")]
    public string? Process { get; set; }

    [JsonPropertyName("windowTitle")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? WindowTitle { get; set; }

    [JsonPropertyName("detail")]
    public string? Detail { get; set; }
}

public sealed class SelfTestReport
{
    [JsonPropertyName("_metadata")]
    public TraceMetadata Metadata { get; set; } = new();

    [JsonPropertyName("ok")]
    public bool Ok { get; set; }

    [JsonPropertyName("version")]
    public string Version { get; set; } = AppIdentity.Version;

    [JsonPropertyName("checks")]
    public Dictionary<string, object?> Checks { get; set; } = new();
}
