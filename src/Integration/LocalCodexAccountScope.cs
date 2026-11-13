/*
Traceability metadata
Version: 1.0
Generated / Last updated / Content coverage through: 2026-11-13 (Asia/Shanghai)
Source type: RAW | user report of missing Codex task and usage displays; LOCAL-WORK | Codex Desktop / Lumi Companion / Codex
Organizing node: Windows / Codex Desktop / Codex
Original sources: legacy Lumi identity lookup and observed owner-scoped Codex Desktop state structure
*/
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace LumiCompanion.Integration;

internal sealed record LocalCodexAccountScope(string AccountId, string AccountUserId)
{
    private const int MaximumStateBytes = 8 * 1024 * 1024;
    private const string RendererOwnerProperty = "electron-renderer-content-owner";
    private const string OwnerAtomsPrefix = "electron-owner-atoms:";

    public string ScopeKey { get; } = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(AccountUserId)));

    public static LocalCodexAccountScope? Resolve(string codexRoot)
    {
        try
        {
            using var stream = new FileStream(Path.Combine(codexRoot, ".codex-global-state.json"),
                FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (stream.Length > MaximumStateBytes) return null;
            using var state = JsonDocument.Parse(stream);
            var root = state.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;

            string atomsProperty = "electron-persisted-atom-state";
            if (root.TryGetProperty(RendererOwnerProperty, out var rendererOwner))
            {
                // The renderer selects the active owner's bucket. A stale legacy identity is unsafe here.
                if (rendererOwner.ValueKind != JsonValueKind.String ||
                    rendererOwner.GetString() is not { Length: > 0 } owner || string.IsNullOrWhiteSpace(owner))
                    return null;
                atomsProperty = OwnerAtomsPrefix + owner;
            }

            if (!root.TryGetProperty(atomsProperty, out var atoms) || atoms.ValueKind != JsonValueKind.Object ||
                !atoms.TryGetProperty("local-document-identity", out var identity) ||
                identity.ValueKind != JsonValueKind.Object ||
                ReadNonblankString(identity, "accountId") is not { } accountId ||
                ReadNonblankString(identity, "accountUserId") is not { } accountUserId)
                return null;
            return new LocalCodexAccountScope(accountId, accountUserId);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    // Keep identifiers out of any diagnostics that format this object.
    public override string ToString() => nameof(LocalCodexAccountScope);

    private static string? ReadNonblankString(JsonElement item, string property)
    {
        if (!item.TryGetProperty(property, out var field) || field.ValueKind != JsonValueKind.String) return null;
        string? value = field.GetString();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }
}
