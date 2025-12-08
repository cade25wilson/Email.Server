using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Email.Server.DTOs.Requests;

public class SendEmailRequest
{
    [Required]
    [EmailAddress]
    [MaxLength(320)]
    [JsonPropertyName("from_email")]
    public required string FromEmail { get; set; }

    [MaxLength(200)]
    [JsonPropertyName("from_name")]
    public string? FromName { get; set; }

    [Required]
    [JsonPropertyName("to")]
    public required List<EmailRecipient> To { get; set; }

    [JsonPropertyName("cc")]
    public List<EmailRecipient>? Cc { get; set; }

    [JsonPropertyName("bcc")]
    public List<EmailRecipient>? Bcc { get; set; }

    [Required]
    [MaxLength(998)]
    [JsonPropertyName("subject")]
    public required string Subject { get; set; }

    [JsonPropertyName("html_body")]
    public string? HtmlBody { get; set; }

    [JsonPropertyName("text_body")]
    public string? TextBody { get; set; }

    [JsonPropertyName("tags")]
    public Dictionary<string, string>? Tags { get; set; }

    [JsonPropertyName("config_set_id")]
    public Guid? ConfigSetId { get; set; }

    [JsonPropertyName("template_id")]
    public Guid? TemplateId { get; set; }

    /// <summary>
    /// Variables to substitute in the template. Keys should match {{variable_name}} placeholders.
    /// </summary>
    [JsonPropertyName("template_variables")]
    public Dictionary<string, string>? TemplateVariables { get; set; }

    /// <summary>
    /// Optional: Schedule the email to be sent at a future time (UTC).
    /// If not provided, email is sent immediately.
    /// </summary>
    [JsonPropertyName("scheduled_at_utc")]
    public DateTime? ScheduledAtUtc { get; set; }
}

public class EmailRecipient
{
    [Required]
    [EmailAddress]
    [MaxLength(320)]
    [JsonPropertyName("email")]
    public required string Email { get; set; }

    [MaxLength(200)]
    [JsonPropertyName("name")]
    public string? Name { get; set; }
}
