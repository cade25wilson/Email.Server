using System.Text.Json.Serialization;
using Email.Server.Models;

namespace Email.Server.DTOs.Responses;

public class SesRegionHealthResponse
{
    [JsonPropertyName("id")]
    public Guid Id { get; set; }

    [JsonPropertyName("region")]
    public string Region { get; set; } = string.Empty;

    [JsonPropertyName("aws_ses_tenant_name")]
    public string? AwsSesTenantName { get; set; }

    [JsonPropertyName("aws_ses_tenant_id")]
    public string? AwsSesTenantId { get; set; }

    [JsonPropertyName("aws_ses_tenant_arn")]
    public string? AwsSesTenantArn { get; set; }

    [JsonPropertyName("sending_status")]
    public string? SendingStatus { get; set; }

    [JsonPropertyName("ses_tenant_created_at")]
    public DateTime? SesTenantCreatedAt { get; set; }

    [JsonPropertyName("provisioning_status")]
    public ProvisioningStatus ProvisioningStatus { get; set; }

    [JsonPropertyName("provisioning_error_message")]
    public string? ProvisioningErrorMessage { get; set; }

    [JsonPropertyName("last_status_check_utc")]
    public DateTime? LastStatusCheckUtc { get; set; }

    [JsonPropertyName("created_at_utc")]
    public DateTime CreatedAtUtc { get; set; }
}

public class TenantSesHealthResponse
{
    [JsonPropertyName("tenant_id")]
    public Guid TenantId { get; set; }

    [JsonPropertyName("tenant_name")]
    public string TenantName { get; set; } = string.Empty;

    [JsonPropertyName("regions")]
    public List<SesRegionHealthResponse> Regions { get; set; } = new();

    [JsonPropertyName("total_regions")]
    public int TotalRegions { get; set; }

    [JsonPropertyName("provisioned_regions")]
    public int ProvisionedRegions { get; set; }

    [JsonPropertyName("failed_regions")]
    public int FailedRegions { get; set; }

    [JsonPropertyName("pending_regions")]
    public int PendingRegions { get; set; }
}
