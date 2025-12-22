namespace Email.Server.Configuration;

public class AwsSmsSettings
{
    public const string SectionName = "AwsSms";

    /// <summary>
    /// AWS Region for SNS SMS service (e.g., "us-east-1")
    /// Note: SMS is only available in certain regions
    /// </summary>
    public string Region { get; set; } = "us-east-1";

    /// <summary>
    /// Optional sender ID to display as the "from" number.
    /// Only supported in certain countries (not US/Canada).
    /// If not set, AWS will use a shared route number.
    /// </summary>
    public string? SenderId { get; set; }

    /// <summary>
    /// Default message type: Transactional or Promotional
    /// Transactional: Higher delivery priority (OTPs, alerts)
    /// Promotional: Standard priority (marketing)
    /// </summary>
    public string DefaultMessageType { get; set; } = "Transactional";
}
