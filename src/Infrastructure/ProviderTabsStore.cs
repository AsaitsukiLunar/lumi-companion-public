// Version: 0.3.2-preview
// Generated/Updated/Coverage: 2026-10-30 (Asia/Shanghai)
// Source type: RAW (user settings), LOCAL-WORK (implementation).
// Node: Windows / Codex Desktop / Codex. Original source: current Lumi provider-tab request.
using System.Text.Json;
using LumiCompanion.Models;

namespace LumiCompanion.Infrastructure;

/// <summary>Local dashboard links only. Does not read browser sessions, credentials, or provider APIs.</summary>
public sealed class ProviderTabsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
    public string FilePath { get; }

    public ProviderTabsStore(string? dataDirectory = null)
    {
        string directory = dataDirectory ?? StorageService.DefaultDataDirectory;
        FilePath = Path.Combine(directory, "provider-tabs.json");
    }

    public ProviderTabsDocument Load(out string? problem)
    {
        problem = null;
        if (!File.Exists(FilePath)) return new ProviderTabsDocument();
        try
        {
            var document = JsonSerializer.Deserialize<ProviderTabsDocument>(File.ReadAllText(FilePath), JsonOptions);
            if (document is null || document.SchemaVersion != 1) throw new InvalidDataException();
            Normalize(document);
            return document;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        {
            // Keep the file intact. Never echo saved dashboard URLs into logs or errors.
            problem = "标签配置暂不可读，已显示默认入口；原文件未改动。";
            return new ProviderTabsDocument();
        }
    }

    public void Save(ProviderTabsDocument document)
    {
        Normalize(document);
        document.Metadata ??= new ProviderTabsMetadata();
        document.Metadata.LastUpdatedAt = DateTimeOffset.Now.ToString("O");
        document.Metadata.ContentCoverageThrough = DateTimeOffset.Now.ToString("yyyy-MM-dd");
        string directory = Path.GetDirectoryName(FilePath)!;
        Directory.CreateDirectory(directory);
        string temporary = FilePath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(document, JsonOptions));
        if (File.Exists(FilePath)) File.Replace(temporary, FilePath, FilePath + ".bak");
        else File.Move(temporary, FilePath);
    }

    public static bool TryGetDashboardUri(string? input, out Uri? uri)
    {
        uri = null;
        string text = input?.Trim() ?? string.Empty;
        if (!Uri.TryCreate(text, UriKind.Absolute, out Uri? candidate) ||
            (candidate.Scheme != Uri.UriSchemeHttps && candidate.Scheme != Uri.UriSchemeHttp) ||
            string.IsNullOrWhiteSpace(candidate.Host) || !string.IsNullOrEmpty(candidate.UserInfo)) return false;
        uri = candidate;
        return true;
    }

    public static void Normalize(ProviderTabsDocument document)
    {
        document.Tabs ??= new List<ProviderTab>();
        var valid = document.Tabs.Where(tab => tab is not null).ToList();
        ProviderTab codex = valid.FirstOrDefault(tab => tab.IsCodex) ?? ProviderTabsDocument.Defaults()[0];
        valid.RemoveAll(tab => tab.IsCodex);
        valid.Insert(0, codex);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (ProviderTab tab in valid)
        {
            if (string.IsNullOrWhiteSpace(tab.Id) || !ids.Add(tab.Id))
            {
                tab.Id = Guid.NewGuid().ToString("N");
                ids.Add(tab.Id);
            }
            tab.Name = (tab.Name ?? string.Empty).Trim();
            if (tab.Name.Length == 0) tab.Name = tab.IsCodex ? "Codex" : "用量入口";
            if (tab.Name.Length > 48) tab.Name = tab.Name[..48];
            tab.DashboardUrl = TryGetDashboardUri(tab.DashboardUrl, out Uri? uri) ? uri!.AbsoluteUri : string.Empty;
        }
        document.Tabs = valid;
        if (!valid.Any(tab => tab.Id == document.SelectedTabId)) document.SelectedTabId = "codex";
    }
}
