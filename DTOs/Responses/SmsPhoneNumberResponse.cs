using System.Text.Json.Serialization;

namespace Email.Server.DTOs.Responses;

public class SmsPhoneNumberResponse
{
    [JsonPropertyName("id")]
    public Guid Id { get; set; }

    [JsonPropertyName("phone_number")]
    public string PhoneNumber { get; set; } = string.Empty;

    [JsonPropertyName("number_type")]
    public string NumberType { get; set; } = string.Empty;

    [JsonPropertyName("number_type_display")]
    public string NumberTypeDisplay => NumberType switch
    {
        "TollFree" => "Toll-Free",
        "ShortCode" => "Short Code",
        _ => "Long Code"
    };

    [JsonPropertyName("country")]
    public string Country { get; set; } = "US";

    [JsonPropertyName("monthly_fee_cents")]
    public int MonthlyFeeCents { get; set; }

    [JsonPropertyName("monthly_fee_display")]
    public string MonthlyFeeDisplay => $"${MonthlyFeeCents / 100m:F2}/mo";

    [JsonPropertyName("is_default")]
    public bool IsDefault { get; set; }

    [JsonPropertyName("is_active")]
    public bool IsActive { get; set; }

    [JsonPropertyName("provisioned_at_utc")]
    public DateTime? ProvisionedAtUtc { get; set; }

    [JsonPropertyName("created_at_utc")]
    public DateTime CreatedAtUtc { get; set; }
}

public class SmsPhoneNumberListResponse
{
    [JsonPropertyName("phone_numbers")]
    public List<SmsPhoneNumberResponse> PhoneNumbers { get; set; } = [];

    [JsonPropertyName("total")]
    public int Total { get; set; }
}
