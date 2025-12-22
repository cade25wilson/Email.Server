using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Email.Server.DTOs.Requests;

public class SendSmsRequest
{
    /// <summary>
    /// Recipient phone number in E.164 format (e.g., +12025551234)
    /// </summary>
    [Required]
    [MaxLength(20)]
    [RegularExpression(@"^\+[1-9]\d{1,14}$", ErrorMessage = "Phone number must be in E.164 format (e.g., +12025551234)")]
    [JsonPropertyName("to")]
    public required string To { get; set; }

    /// <summary>
    /// Optional: Sender phone number. If not specified, uses tenant's default phone number.
    /// </summary>
    [MaxLength(20)]
    [JsonPropertyName("from")]
    public string? From { get; set; }

    /// <summary>
    /// SMS message body. Max 1600 characters (10 SMS segments).
    /// </summary>
    [Required]
    [MaxLength(1600)]
    [JsonPropertyName("body")]
    public required string Body { get; set; }

    /// <summary>
    /// Optional: Template ID to use for the message body.
    /// </summary>
    [JsonPropertyName("template_id")]
    public Guid? TemplateId { get; set; }

    /// <summary>
    /// Variables to substitute in the template (e.g., {"name": "John", "code": "123456"}).
    /// Variables in templates use the format {{variable_name}}.
    /// </summary>
    [JsonPropertyName("template_variables")]
    public Dictionary<string, string>? TemplateVariables { get; set; }

    /// <summary>
    /// Optional: Schedule the SMS to be sent at a future time (UTC).
    /// If not provided, SMS is sent immediately.
    /// </summary>
    [JsonPropertyName("scheduled_at_utc")]
    public DateTime? ScheduledAtUtc { get; set; }
}

public class SmsQueryParams
{
    [JsonPropertyName("page")]
    public int Page { get; set; } = 1;

    [JsonPropertyName("page_size")]
    public int PageSize { get; set; } = 20;

    [JsonPropertyName("status")]
    public byte? Status { get; set; }

    [JsonPropertyName("from")]
    public DateTime? From { get; set; }

    [JsonPropertyName("to")]
    public DateTime? To { get; set; }
}
