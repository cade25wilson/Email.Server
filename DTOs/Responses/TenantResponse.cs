using System.Text.Json.Serialization;
using Email.Server.Models;

namespace Email.Server.DTOs.Responses;

public class TenantResponse
{
    [JsonPropertyName("id")]
    public Guid Id { get; set; }

    [JsonPropertyName("name")]
    public required string Name { get; set; }

    [JsonPropertyName("status")]
    public TenantStatus Status { get; set; }

    [JsonPropertyName("created_at_utc")]
    public DateTime CreatedAtUtc { get; set; }

    [JsonPropertyName("member_count")]
    public int MemberCount { get; set; }

    [JsonPropertyName("current_user_role")]
    public TenantRole? CurrentUserRole { get; set; }
}