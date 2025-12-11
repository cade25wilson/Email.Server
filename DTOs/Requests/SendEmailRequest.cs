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

    /// <summary>
    /// Optional: File attachments to include with the email.
    /// Total attachment size should not exceed 10MB.
    /// </summary>
    [JsonPropertyName("attachments")]
    public List<EmailAttachment>? Attachments { get; set; }
}

public class EmailAttachment
{
    /// <summary>
    /// The filename to display in the email (e.g., "report.pdf")
    /// </summary>
    [Required]
    [MaxLength(255)]
    [JsonPropertyName("filename")]
    public required string Filename { get; set; }

    /// <summary>
    /// Base64-encoded content of the file. Either content or path is required.
    /// </summary>
    [JsonPropertyName("content")]
    public string? Content { get; set; }

    /// <summary>
    /// URL to fetch the attachment from. Either content or path is required.
    /// The file will be downloaded and attached to the email.
    /// </summary>
    [MaxLength(2048)]
    [JsonPropertyName("path")]
    public string? Path { get; set; }

    /// <summary>
    /// MIME type of the attachment (e.g., "application/pdf", "image/png").
    /// Optional when using path - will be inferred from response headers if not provided.
    /// </summary>
    [MaxLength(127)]
    [JsonPropertyName("content_type")]
    public string? ContentType { get; set; }
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
