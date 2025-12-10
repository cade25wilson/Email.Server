using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Email.Server.DTOs.Requests;

/// <summary>
/// Request to update a scheduled email. Only emails with Status=4 (Scheduled) can be updated.
/// </summary>
public class UpdateScheduledEmailRequest
{
    [MaxLength(998)]
    [JsonPropertyName("subject")]
    public string? Subject { get; set; }

    [JsonPropertyName("html_body")]
    public string? HtmlBody { get; set; }

    [JsonPropertyName("text_body")]
    public string? TextBody { get; set; }

    /// <summary>
    /// New scheduled send time (UTC). Must be in the future.
    /// </summary>
    [JsonPropertyName("scheduled_at_utc")]
    public DateTime? ScheduledAtUtc { get; set; }

    [JsonPropertyName("tags")]
    public Dictionary<string, string>? Tags { get; set; }
}
