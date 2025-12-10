using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Email.Server.DTOs.Requests;

public class ListEmailsRequest
{
    /// <summary>
    /// Page number (1-based). Default: 1
    /// </summary>
    [Range(1, int.MaxValue)]
    [JsonPropertyName("page")]
    public int Page { get; set; } = 1;

    /// <summary>
    /// Number of items per page. Default: 20, Max: 100
    /// </summary>
    [Range(1, 100)]
    [JsonPropertyName("page_size")]
    public int PageSize { get; set; } = 20;

    /// <summary>
    /// Filter by status: 0=Queued, 1=Sent, 2=Failed, 3=Partial, 4=Scheduled, 5=Cancelled
    /// </summary>
    [JsonPropertyName("status")]
    public byte? Status { get; set; }

    /// <summary>
    /// Filter by sender email (partial match)
    /// </summary>
    [MaxLength(320)]
    [JsonPropertyName("from_email")]
    public string? FromEmail { get; set; }

    /// <summary>
    /// Filter by recipient email (partial match)
    /// </summary>
    [MaxLength(320)]
    [JsonPropertyName("to_email")]
    public string? ToEmail { get; set; }

    /// <summary>
    /// Filter by emails sent after this date (UTC)
    /// </summary>
    [JsonPropertyName("since")]
    public DateTime? Since { get; set; }

    /// <summary>
    /// Filter by emails sent before this date (UTC)
    /// </summary>
    [JsonPropertyName("until")]
    public DateTime? Until { get; set; }

    /// <summary>
    /// Sort field: requested_at, sent_at, subject. Default: requested_at
    /// </summary>
    [JsonPropertyName("sort_by")]
    public string SortBy { get; set; } = "requested_at";

    /// <summary>
    /// Sort order: asc, desc. Default: desc
    /// </summary>
    [JsonPropertyName("sort_order")]
    public string SortOrder { get; set; } = "desc";
}
