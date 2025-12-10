using Azure.Storage.Blobs;
using Azure.Storage.Sas;
using Email.Server.Services.Interfaces;

namespace Email.Server.Services.Implementations;

public class AttachmentStorageService : IAttachmentStorageService
{
    private readonly BlobServiceClient _blobServiceClient;
    private readonly ILogger<AttachmentStorageService> _logger;
    private const string ContainerName = "email-attachments";
    private static readonly TimeSpan SasExpiry = TimeSpan.FromHours(1);

    public AttachmentStorageService(
        BlobServiceClient blobServiceClient,
        ILogger<AttachmentStorageService> logger)
    {
        _blobServiceClient = blobServiceClient;
        _logger = logger;
    }

    public async Task<string> UploadAttachmentAsync(
        Guid tenantId,
        Guid messageId,
        string fileName,
        string contentType,
        Stream stream,
        CancellationToken cancellationToken = default)
    {
        var containerClient = _blobServiceClient.GetBlobContainerClient(ContainerName);
        await containerClient.CreateIfNotExistsAsync(cancellationToken: cancellationToken);

        // Organize by tenant/message for easy cleanup
        var blobName = $"{tenantId}/{messageId}/{Guid.NewGuid()}/{fileName}";
        var blobClient = containerClient.GetBlobClient(blobName);

        await blobClient.UploadAsync(
            stream,
            new Azure.Storage.Blobs.Models.BlobHttpHeaders { ContentType = contentType },
            cancellationToken: cancellationToken);

        _logger.LogInformation(
            "Uploaded attachment {FileName} for message {MessageId}, size {Size} bytes",
            fileName, messageId, stream.Length);

        return blobClient.Uri.ToString();
    }

    public Task<(string Url, DateTime ExpiresAt)> GetSignedDownloadUrlAsync(
        string blobUrl,
        CancellationToken cancellationToken = default)
    {
        var blobUri = new Uri(blobUrl);

        // Extract the blob path from the URL and get the client from our service
        var containerClient = _blobServiceClient.GetBlobContainerClient(ContainerName);
        var pathAfterContainer = blobUri.AbsolutePath.TrimStart('/');
        var containerPrefix = $"{ContainerName}/";
        var blobPath = pathAfterContainer.StartsWith(containerPrefix)
            ? pathAfterContainer[containerPrefix.Length..]
            : pathAfterContainer;
        var blobClient = containerClient.GetBlobClient(blobPath);

        if (!blobClient.CanGenerateSasUri)
        {
            throw new InvalidOperationException("Cannot generate SAS token. Ensure storage account connection string includes account key.");
        }

        var expiresAt = DateTime.UtcNow.Add(SasExpiry);
        var sasBuilder = new BlobSasBuilder
        {
            BlobContainerName = ContainerName,
            BlobName = blobPath,
            Resource = "b",
            ExpiresOn = expiresAt
        };
        sasBuilder.SetPermissions(BlobSasPermissions.Read);

        var sasUri = blobClient.GenerateSasUri(sasBuilder);

        return Task.FromResult((sasUri.ToString(), expiresAt));
    }

    public async Task DeleteAttachmentAsync(string blobUrl, CancellationToken cancellationToken = default)
    {
        var blobUri = new Uri(blobUrl);
        var containerClient = _blobServiceClient.GetBlobContainerClient(ContainerName);
        var blobPath = blobUri.AbsolutePath.Replace($"/{ContainerName}/", "");
        var blobClient = containerClient.GetBlobClient(blobPath);

        await blobClient.DeleteIfExistsAsync(cancellationToken: cancellationToken);

        _logger.LogInformation("Deleted attachment {BlobUrl}", blobUrl);
    }
}
