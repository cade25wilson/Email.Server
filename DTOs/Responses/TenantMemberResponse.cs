using System.Text.Json.Serialization;
using Email.Server.Models;

namespace Email.Server.DTOs.Responses;

public class TenantMemberResponse
{
    [JsonPropertyName("user_id")]
    public required string UserId { get; set; }

    [JsonPropertyName("email")]
    public required string Email { get; set; }

    [JsonPropertyName("name")]
    public required string Name { get; set; }

    [JsonPropertyName("role")]
    public TenantRole Role { get; set; }

    [JsonPropertyName("joined_at_utc")]
    public DateTime JoinedAtUtc { get; set; }
}