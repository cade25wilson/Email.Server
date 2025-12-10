using System.Text.Json.Serialization;

namespace Email.Server.DTOs.Responses;

public class CreateApiKeyResponse
{
    [JsonPropertyName("id")]
    public Guid Id { get; set; }

    [JsonPropertyName("name")]
    public required string Name { get; set; }

    [JsonPropertyName("key")]
    public required string Key { get; set; }  // Full key - shown ONCE

    [JsonPropertyName("key_preview")]
    public required string KeyPreview { get; set; }  // "sk_a1b2c3d4_..."

    [JsonPropertyName("scopes")]
    public required List<string> Scopes { get; set; }

    [JsonPropertyName("domain_id")]
    public Guid? DomainId { get; set; }

    [JsonPropertyName("domain_name")]
    public string? DomainName { get; set; }

    [JsonPropertyName("created_at_utc")]
    public DateTime CreatedAtUtc { get; set; }
}

public class ApiKeyListItemResponse
{
    [JsonPropertyName("id")]
    public Guid Id { get; set; }

    [JsonPropertyName("name")]
    public required string Name { get; set; }

    [JsonPropertyName("key_preview")]
    public required string KeyPreview { get; set; }

    [JsonPropertyName("scopes")]
    public required List<string> Scopes { get; set; }

    [JsonPropertyName("domain_id")]
    public Guid? DomainId { get; set; }

    [JsonPropertyName("domain_name")]
    public string? DomainName { get; set; }

    [JsonPropertyName("created_at_utc")]
    public DateTime CreatedAtUtc { get; set; }

    [JsonPropertyName("last_used_at_utc")]
    public DateTime? LastUsedAtUtc { get; set; }

    [JsonPropertyName("is_revoked")]
    public bool IsRevoked { get; set; }
}
