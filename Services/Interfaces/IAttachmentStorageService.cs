namespace Email.Server.Services.Interfaces;

public interface IAttachmentStorageService
{
    /// <summary>
    /// Uploads an attachment to blob storage
    /// </summary>
    /// <param name="tenantId">Tenant ID for organization</param>
    /// <param name="messageId">Message ID the attachment belongs to</param>
    /// <param name="fileName">Original file name</param>
    /// <param name="contentType">MIME content type</param>
    /// <param name="stream">File content stream</param>
    /// <returns>The blob URL where the file was stored</returns>
    Task<string> UploadAttachmentAsync(
        Guid tenantId,
        Guid messageId,
        string fileName,
        string contentType,
        Stream stream,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a signed download URL for an attachment (1 hour expiry)
    /// </summary>
    /// <param name="blobUrl">The blob URL stored in the database</param>
    /// <returns>Signed download URL and expiry time</returns>
    Task<(string Url, DateTime ExpiresAt)> GetSignedDownloadUrlAsync(
        string blobUrl,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes an attachment from blob storage
    /// </summary>
    Task DeleteAttachmentAsync(string blobUrl, CancellationToken cancellationToken = default);
}
