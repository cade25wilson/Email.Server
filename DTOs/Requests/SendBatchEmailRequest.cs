using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Email.Server.DTOs.Requests;

public class SendBatchEmailRequest
{
    /// <summary>
    /// List of emails to send. Maximum 100 emails per batch.
    /// </summary>
    [Required]
    [MaxLength(100)]
    [JsonPropertyName("emails")]
    public required List<SendEmailRequest> Emails { get; set; }
}
