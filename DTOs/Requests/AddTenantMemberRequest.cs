using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Email.Server.Models;

namespace Email.Server.DTOs.Requests;

public class AddTenantMemberRequest
{
    [Required]
    [EmailAddress]
    [JsonPropertyName("user_email")]
    public required string UserEmail { get; set; }

    [Required]
    [JsonPropertyName("role")]
    public TenantRole Role { get; set; }
}