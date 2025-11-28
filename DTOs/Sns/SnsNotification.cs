using System.Text.Json.Serialization;

namespace Email.Server.DTOs.Sns;

/// <summary>
/// Base SNS message wrapper
/// </summary>
public class SnsMessage
{
    [JsonPropertyName("Type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("MessageId")]
    public string MessageId { get; set; } = string.Empty;

    [JsonPropertyName("TopicArn")]
    public string TopicArn { get; set; } = string.Empty;

    [JsonPropertyName("Message")]
    public string Message { get; set; } = string.Empty;

    [JsonPropertyName("Timestamp")]
    public DateTime Timestamp { get; set; }

    [JsonPropertyName("SignatureVersion")]
    public string? SignatureVersion { get; set; }

    [JsonPropertyName("Signature")]
    public string? Signature { get; set; }

    [JsonPropertyName("SigningCertURL")]
    public string? SigningCertUrl { get; set; }

    [JsonPropertyName("SubscribeURL")]
    public string? SubscribeUrl { get; set; }

    [JsonPropertyName("UnsubscribeURL")]
    public string? UnsubscribeUrl { get; set; }
}

/// <summary>
/// SES notification contained within SNS message
/// </summary>
public class SesNotification
{
    [JsonPropertyName("notificationType")]
    public string NotificationType { get; set; } = string.Empty;

    [JsonPropertyName("mail")]
    public SesMail Mail { get; set; } = new();

    [JsonPropertyName("bounce")]
    public SesBounce? Bounce { get; set; }

    [JsonPropertyName("complaint")]
    public SesComplaint? Complaint { get; set; }

    [JsonPropertyName("delivery")]
    public SesDelivery? Delivery { get; set; }
}

public class SesMail
{
    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; set; }

    [JsonPropertyName("messageId")]
    public string MessageId { get; set; } = string.Empty;

    [JsonPropertyName("source")]
    public string Source { get; set; } = string.Empty;

    [JsonPropertyName("sourceArn")]
    public string? SourceArn { get; set; }

    [JsonPropertyName("destination")]
    public List<string> Destination { get; set; } = new();

    [JsonPropertyName("headersTruncated")]
    public bool HeadersTruncated { get; set; }

    [JsonPropertyName("headers")]
    public List<SesHeader>? Headers { get; set; }

    [JsonPropertyName("commonHeaders")]
    public SesCommonHeaders? CommonHeaders { get; set; }
}

public class SesHeader
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("value")]
    public string Value { get; set; } = string.Empty;
}

public class SesCommonHeaders
{
    [JsonPropertyName("from")]
    public List<string>? From { get; set; }

    [JsonPropertyName("to")]
    public List<string>? To { get; set; }

    [JsonPropertyName("subject")]
    public string? Subject { get; set; }
}

public class SesBounce
{
    [JsonPropertyName("bounceType")]
    public string BounceType { get; set; } = string.Empty;

    [JsonPropertyName("bounceSubType")]
    public string BounceSubType { get; set; } = string.Empty;

    [JsonPropertyName("bouncedRecipients")]
    public List<SesBouncedRecipient> BouncedRecipients { get; set; } = new();

    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; set; }

    [JsonPropertyName("feedbackId")]
    public string FeedbackId { get; set; } = string.Empty;

    [JsonPropertyName("reportingMTA")]
    public string? ReportingMta { get; set; }
}

public class SesBouncedRecipient
{
    [JsonPropertyName("emailAddress")]
    public string EmailAddress { get; set; } = string.Empty;

    [JsonPropertyName("action")]
    public string? Action { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("diagnosticCode")]
    public string? DiagnosticCode { get; set; }
}

public class SesComplaint
{
    [JsonPropertyName("complainedRecipients")]
    public List<SesComplainedRecipient> ComplainedRecipients { get; set; } = new();

    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; set; }

    [JsonPropertyName("feedbackId")]
    public string FeedbackId { get; set; } = string.Empty;

    [JsonPropertyName("complaintSubType")]
    public string? ComplaintSubType { get; set; }

    [JsonPropertyName("complaintFeedbackType")]
    public string? ComplaintFeedbackType { get; set; }
}

public class SesComplainedRecipient
{
    [JsonPropertyName("emailAddress")]
    public string EmailAddress { get; set; } = string.Empty;
}

public class SesDelivery
{
    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; set; }

    [JsonPropertyName("processingTimeMillis")]
    public int ProcessingTimeMillis { get; set; }

    [JsonPropertyName("recipients")]
    public List<string> Recipients { get; set; } = new();

    [JsonPropertyName("smtpResponse")]
    public string? SmtpResponse { get; set; }

    [JsonPropertyName("reportingMTA")]
    public string? ReportingMta { get; set; }
}
