using System.Text.Json.Serialization;

namespace Email.Server.DTOs.Responses;

public class DomainResponse
{
    [JsonPropertyName("id")]
    public Guid Id { get; set; }

    [JsonPropertyName("domain")]
    public required string Domain { get; set; }

    [JsonPropertyName("region")]
    public required string Region { get; set; }

    [JsonPropertyName("verification_status")]
    public byte VerificationStatus { get; set; } // 0=Pending,1=Verified,2=Failed

    [JsonPropertyName("verification_status_text")]
    public string VerificationStatusText => VerificationStatus switch
    {
        1 => "Verified",
        2 => "Failed",
        _ => "Pending"
    };

    [JsonPropertyName("dkim_status")]
    public byte DkimStatus { get; set; } // 0=Pending,1=Success,2=Failed

    [JsonPropertyName("dkim_status_text")]
    public string DkimStatusText => DkimStatus switch
    {
        1 => "Success",
        2 => "Failed",
        _ => "Pending"
    };

    [JsonPropertyName("mail_from_status")]
    public byte MailFromStatus { get; set; }

    [JsonPropertyName("mail_from_subdomain")]
    public string? MailFromSubdomain { get; set; }

    [JsonPropertyName("identity_arn")]
    public string? IdentityArn { get; set; }

    [JsonPropertyName("created_at_utc")]
    public DateTime CreatedAtUtc { get; set; }

    [JsonPropertyName("verified_at_utc")]
    public DateTime? VerifiedAtUtc { get; set; }

    [JsonPropertyName("inbound_enabled")]
    public bool InboundEnabled { get; set; }

    [JsonPropertyName("inbound_status")]
    public byte InboundStatus { get; set; } // 0=Off, 1=Pending, 2=Active

    [JsonPropertyName("inbound_status_text")]
    public string InboundStatusText => InboundStatus switch
    {
        1 => "Pending",
        2 => "Active",
        _ => "Off"
    };

    [JsonPropertyName("dns_records")]
    public List<DnsRecordResponse> DnsRecords { get; set; } = new();
}
