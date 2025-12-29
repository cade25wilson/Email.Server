namespace Email.Server.Services.Interfaces;

/// <summary>
/// Low-level client wrapper for AWS SMS operations.
/// Supports both SNS (shared routes) and Pinpoint SMS Voice V2 (tenant pools).
/// </summary>
public interface ISmsClientService
{
    /// <summary>
    /// Sends an SMS message via AWS SNS (shared routes).
    /// </summary>
    /// <param name="toNumber">The recipient phone number (E.164 format)</param>
    /// <param name="body">The SMS message body</param>
    /// <param name="isTransactional">True for OTPs/critical alerts, false for promotional</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>AWS message ID and segment count</returns>
    Task<SmsSendResult> SendSmsAsync(string toNumber, string body, bool isTransactional = true, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends an SMS message via AWS Pinpoint SMS Voice V2 using a tenant's pool.
    /// AWS automatically selects a phone number from the pool for failover/distribution.
    /// </summary>
    /// <param name="poolArn">The AWS Pool ARN or ID to use as origination identity</param>
    /// <param name="toNumber">The recipient phone number (E.164 format)</param>
    /// <param name="body">The SMS message body</param>
    /// <param name="isTransactional">True for OTPs/critical alerts, false for promotional</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>AWS message ID and segment count</returns>
    Task<SmsSendResult> SendSmsViaPoolAsync(string poolArn, string toNumber, string body, bool isTransactional = true, CancellationToken cancellationToken = default);

    /// <summary>
    /// Calculates the number of SMS segments for a message body.
    /// </summary>
    int CalculateSegmentCount(string body);
}

public class SmsSendResult
{
    public bool Success { get; set; }
    public string? MessageId { get; set; }
    public int SegmentCount { get; set; }
    public string? Error { get; set; }
}

public class PhoneNumberInfo
{
    public string PhoneNumber { get; set; } = string.Empty;
    public string NumberType { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public bool IsValid { get; set; }
}
