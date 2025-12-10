using System.Text.Json.Serialization;

namespace Email.Server.DTOs.Sns;

/// <summary>
/// SES notification for received (inbound) emails
/// </summary>
public class SesReceiveNotification
{
    [JsonPropertyName("notificationType")]
    public string NotificationType { get; set; } = string.Empty;

    [JsonPropertyName("receipt")]
    public SesReceiveReceipt? Receipt { get; set; }

    [JsonPropertyName("mail")]
    public SesReceiveMail? Mail { get; set; }
}

public class SesReceiveReceipt
{
    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; set; }

    [JsonPropertyName("processingTimeMillis")]
    public int ProcessingTimeMillis { get; set; }

    [JsonPropertyName("recipients")]
    public List<string> Recipients { get; set; } = [];

    [JsonPropertyName("spamVerdict")]
    public SesVerdict? SpamVerdict { get; set; }

    [JsonPropertyName("virusVerdict")]
    public SesVerdict? VirusVerdict { get; set; }

    [JsonPropertyName("spfVerdict")]
    public SesVerdict? SpfVerdict { get; set; }

    [JsonPropertyName("dkimVerdict")]
    public SesVerdict? DkimVerdict { get; set; }

    [JsonPropertyName("dmarcVerdict")]
    public SesVerdict? DmarcVerdict { get; set; }

    [JsonPropertyName("action")]
    public SesReceiveAction? Action { get; set; }
}

public class SesVerdict
{
    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty; // PASS, FAIL, GRAY, PROCESSING_FAILED
}

public class SesReceiveAction
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty; // S3, SNS, Lambda, etc.

    [JsonPropertyName("bucketName")]
    public string? BucketName { get; set; }

    [JsonPropertyName("objectKey")]
    public string? ObjectKey { get; set; }
}

public class SesReceiveMail
{
    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; set; }

    [JsonPropertyName("messageId")]
    public string MessageId { get; set; } = string.Empty;

    [JsonPropertyName("source")]
    public string Source { get; set; } = string.Empty;

    [JsonPropertyName("destination")]
    public List<string> Destination { get; set; } = [];

    [JsonPropertyName("headersTruncated")]
    public bool HeadersTruncated { get; set; }

    [JsonPropertyName("headers")]
    public List<SesReceiveHeader>? Headers { get; set; }

    [JsonPropertyName("commonHeaders")]
    public SesReceiveCommonHeaders? CommonHeaders { get; set; }
}

public class SesReceiveHeader
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("value")]
    public string Value { get; set; } = string.Empty;
}

public class SesReceiveCommonHeaders
{
    [JsonPropertyName("returnPath")]
    public string? ReturnPath { get; set; }

    [JsonPropertyName("from")]
    public List<string>? From { get; set; }

    [JsonPropertyName("to")]
    public List<string>? To { get; set; }

    [JsonPropertyName("cc")]
    public List<string>? Cc { get; set; }

    [JsonPropertyName("replyTo")]
    public List<string>? ReplyTo { get; set; }

    [JsonPropertyName("date")]
    public string? Date { get; set; }

    [JsonPropertyName("messageId")]
    public string? MessageId { get; set; }

    [JsonPropertyName("subject")]
    public string? Subject { get; set; }
}
