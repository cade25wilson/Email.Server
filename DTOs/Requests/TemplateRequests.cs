using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Email.Server.DTOs.Requests;

public class CreateTemplateRequest
{
    [Required]
    [MaxLength(200)]
    [JsonPropertyName("name")]
    public required string Name { get; set; }

    [MaxLength(998)]
    [JsonPropertyName("subject")]
    public string? Subject { get; set; }

    [JsonPropertyName("html_body")]
    public string? HtmlBody { get; set; }

    [JsonPropertyName("text_body")]
    public string? TextBody { get; set; }
}

public class UpdateTemplateRequest
{
    [MaxLength(200)]
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [MaxLength(998)]
    [JsonPropertyName("subject")]
    public string? Subject { get; set; }

    [JsonPropertyName("html_body")]
    public string? HtmlBody { get; set; }

    [JsonPropertyName("text_body")]
    public string? TextBody { get; set; }
}
