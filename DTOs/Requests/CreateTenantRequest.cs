using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Email.Server.DTOs.Requests;

public class CreateTenantRequest
{
    [Required]
    [StringLength(100, MinimumLength = 1)]
    [JsonPropertyName("name")]
    public required string Name { get; set; }
}