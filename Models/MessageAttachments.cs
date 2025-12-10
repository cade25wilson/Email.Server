using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Email.Server.Models;

public class MessageAttachments
{
    [Key]
    public long Id { get; set; }

    [ForeignKey("Message")]
    public Guid MessageId { get; set; }

    [Required]
    [MaxLength(256)]
    public required string FileName { get; set; }

    [Required]
    [MaxLength(128)]
    public required string ContentType { get; set; }

    public long SizeBytes { get; set; }

    [Required]
    [MaxLength(2048)]
    public required string BlobUrl { get; set; }

    /// <summary>
    /// Content-ID for inline attachments (e.g., embedded images)
    /// </summary>
    [MaxLength(256)]
    public string? ContentId { get; set; }

    /// <summary>
    /// Whether this attachment is inline (embedded in HTML body)
    /// </summary>
    public bool IsInline { get; set; }

    public DateTime UploadedAtUtc { get; set; } = DateTime.UtcNow;

    // Navigation property
    public Messages? Message { get; set; }
}
