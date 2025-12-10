namespace Email.Server.Services.Interfaces;

public interface IInboundEmailStorageService
{
    /// <summary>
    /// Stores an inbound email in S3. Note: For SES inbound emails,
    /// the email is already in S3 - use GetEmailAsync with the SES-provided key.
    /// This method is for manual storage if needed.
    /// </summary>
    Task<string> StoreEmailAsync(
        Guid tenantId,
        Guid messageId,
        Stream content,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the raw email content from S3
    /// </summary>
    /// <param name="s3Key">The S3 key (from SES notification or StoreEmailAsync)</param>
    Task<Stream> GetEmailAsync(
        string s3Key,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a presigned download URL for the raw email (1 hour expiry)
    /// </summary>
    Task<(string Url, DateTime ExpiresAt)> GetSignedDownloadUrlAsync(
        string s3Key,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes an inbound email from S3
    /// </summary>
    Task DeleteEmailAsync(
        string s3Key,
        CancellationToken cancellationToken = default);
}
