namespace Email.Server.Services.Interfaces;

public interface IAttachmentStorageService
{
    /// <summary>
    /// Uploads an attachment to S3 storage
    /// </summary>
    /// <param name="tenantId">Tenant ID for organization</param>
    /// <param name="messageId">Message ID the attachment belongs to</param>
    /// <param name="fileName">Original file name</param>
    /// <param name="contentType">MIME content type</param>
    /// <param name="stream">File content stream</param>
    /// <returns>The S3 key where the file was stored</returns>
    Task<string> UploadAttachmentAsync(
        Guid tenantId,
        Guid messageId,
        string fileName,
        string contentType,
        Stream stream,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a presigned download URL for an attachment (1 hour expiry)
    /// </summary>
    /// <param name="s3Key">The S3 key stored in the database</param>
    /// <returns>Presigned download URL and expiry time</returns>
    Task<(string Url, DateTime ExpiresAt)> GetSignedDownloadUrlAsync(
        string s3Key,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes an attachment from S3 storage
    /// </summary>
    Task DeleteAttachmentAsync(string s3Key, CancellationToken cancellationToken = default);
}
