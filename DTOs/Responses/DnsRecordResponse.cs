using System.Text.Json.Serialization;

namespace Email.Server.DTOs.Responses;

public class DnsRecordResponse
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("record_type")]
    public required string RecordType { get; set; } // TXT, CNAME, MX

    [JsonPropertyName("host")]
    public required string Host { get; set; }

    [JsonPropertyName("value")]
    public required string Value { get; set; }

    [JsonPropertyName("required")]
    public bool Required { get; set; }

    [JsonPropertyName("last_checked_utc")]
    public DateTime? LastCheckedUtc { get; set; }

    [JsonPropertyName("status")]
    public byte Status { get; set; } // 0=Unknown,1=Found,2=Missing,3=Invalid

    [JsonPropertyName("status_text")]
    public string StatusText => Status switch
    {
        1 => "Found",
        2 => "Missing",
        3 => "Invalid",
        _ => "Unknown"
    };
}
