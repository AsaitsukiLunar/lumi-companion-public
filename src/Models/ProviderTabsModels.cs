// Version: 0.3.4-preview
// Generated/Updated/Coverage: 2026-10-30 (Asia/Shanghai)
// Source type: RAW (user provider-tab request/settings), LOCAL-WORK (implementation/defaults).
// Node: Windows / Codex Desktop / Codex. Original source: current Lumi conversation.
using System.Text.Json.Serialization;

namespace LumiCompanion.Models;

public sealed class ProviderTab
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public string DashboardUrl { get; set; } = string.Empty;
    [JsonIgnore] public bool IsCodex => Id == "codex";
    // A built-in fallback is presentation only; it does not migrate or overwrite saved links.
    [JsonIgnore] public string EffectiveDashboardUrl => !string.IsNullOrWhiteSpace(DashboardUrl)
        ? DashboardUrl : Id switch
        {
            "codex" => "https://chatgpt.com/settings/usage",
            "workbuddy" => "https://www.workbuddy.cn/profile/plans-usage",
            _ => string.Empty
        };

    public ProviderTab Copy() => new() { Id = Id, Name = Name, DashboardUrl = DashboardUrl };
}

public sealed class ProviderTabsDocument
{
    [JsonPropertyName("_metadata")]
    public ProviderTabsMetadata Metadata { get; set; } = new();
    public int SchemaVersion { get; set; } = 1;
    public string SelectedTabId { get; set; } = "codex";
    public List<ProviderTab> Tabs { get; set; } = Defaults();

    public ProviderTabsDocument Copy() => new()
    {
        Metadata = Metadata,
        SelectedTabId = SelectedTabId,
        Tabs = Tabs.Select(tab => tab.Copy()).ToList()
    };

    public static List<ProviderTab> Defaults() =>
    [
        new() { Id = "codex", Name = "Codex" },
        new() { Id = "gpt-plus", Name = "GPT Plus" },
        new() { Id = "workbuddy", Name = "WorkBuddy" }
    ];
}

public sealed class ProviderTabsMetadata
{
    public string Version { get; set; } = "1.0";
    public string GeneratedAt { get; set; } = DateTimeOffset.Now.ToString("O");
    public string LastUpdatedAt { get; set; } = DateTimeOffset.Now.ToString("O");
    public string ContentCoverageThrough { get; set; } = "2026-10-30";
    public string SourceType { get; set; } = "RAW | user settings; LOCAL-WORK | generated defaults and implementation";
    public string Source { get; set; } = "Codex Desktop / 云端本地互通文件夹 / current Lumi conversation (title unverified) / Codex";
    public string OrganizingNode { get; set; } = "Windows / Codex Desktop / Codex";
    public string OriginalSources { get; set; } = "User provider names and dashboard links; Codex local default configuration";
}
