using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Email.Server.DTOs.Requests;

public class CreateSmsTemplateRequest
{
    /// <summary>
    /// Unique name for the template within the tenant.
    /// </summary>
    [Required]
    [MaxLength(100)]
    [JsonPropertyName("name")]
    public required string Name { get; set; }

    /// <summary>
    /// SMS message body template. Supports {{variable}} syntax.
    /// Max 1600 characters (10 SMS segments).
    /// </summary>
    [Required]
    [MaxLength(1600)]
    [JsonPropertyName("body")]
    public required string Body { get; set; }
}

public class UpdateSmsTemplateRequest
{
    /// <summary>
    /// Updated name for the template.
    /// </summary>
    [MaxLength(100)]
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>
    /// Updated SMS message body template.
    /// </summary>
    [MaxLength(1600)]
    [JsonPropertyName("body")]
    public string? Body { get; set; }

    /// <summary>
    /// Whether the template is active.
    /// </summary>
    [JsonPropertyName("is_active")]
    public bool? IsActive { get; set; }
}
