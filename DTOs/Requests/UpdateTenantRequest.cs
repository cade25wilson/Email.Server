using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Email.Server.Models;

namespace Email.Server.DTOs.Requests;

public class UpdateTenantRequest
{
    [StringLength(100, MinimumLength = 1)]
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("status")]
    public TenantStatus? Status { get; set; }
}