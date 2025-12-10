using Amazon.S3;
using Amazon.S3.Model;
using Email.Server.Services.Interfaces;

namespace Email.Server.Services.Implementations;

public class InboundEmailStorageService : IInboundEmailStorageService
{
    private readonly IAmazonS3 _s3Client;
    private readonly ILogger<InboundEmailStorageService> _logger;
    private readonly string _bucketName;
    private static readonly TimeSpan PresignedUrlExpiry = TimeSpan.FromHours(1);

    public InboundEmailStorageService(
        IAmazonS3 s3Client,
        IConfiguration configuration,
        ILogger<InboundEmailStorageService> logger)
    {
        _s3Client = s3Client;
        _logger = logger;
        _bucketName = configuration["S3:InboundEmailsBucket"]
            ?? throw new InvalidOperationException("S3:InboundEmailsBucket configuration is required");
    }

    public async Task<string> StoreEmailAsync(
        Guid tenantId,
        Guid messageId,
        Stream content,
        CancellationToken cancellationToken = default)
    {
        // Organize by tenant/year/month for easy management
        var now = DateTime.UtcNow;
        var s3Key = $"inbound/{tenantId}/{now:yyyy}/{now:MM}/{messageId}.eml";

        var request = new PutObjectRequest
        {
            BucketName = _bucketName,
            Key = s3Key,
            InputStream = content,
            ContentType = "message/rfc822"
        };

        await _s3Client.PutObjectAsync(request, cancellationToken);

        _logger.LogInformation(
            "Stored inbound email {MessageId} for tenant {TenantId}, key {S3Key}",
            messageId, tenantId, s3Key);

        return s3Key;
    }

    public async Task<Stream> GetEmailAsync(
        string s3Key,
        CancellationToken cancellationToken = default)
    {
        var request = new GetObjectRequest
        {
            BucketName = _bucketName,
            Key = s3Key
        };

        var response = await _s3Client.GetObjectAsync(request, cancellationToken);
        return response.ResponseStream;
    }

    public Task<(string Url, DateTime ExpiresAt)> GetSignedDownloadUrlAsync(
        string s3Key,
        CancellationToken cancellationToken = default)
    {
        var expiresAt = DateTime.UtcNow.Add(PresignedUrlExpiry);

        var request = new GetPreSignedUrlRequest
        {
            BucketName = _bucketName,
            Key = s3Key,
            Expires = expiresAt,
            Verb = HttpVerb.GET
        };

        var url = _s3Client.GetPreSignedURL(request);

        return Task.FromResult((url, expiresAt));
    }

    public async Task DeleteEmailAsync(
        string s3Key,
        CancellationToken cancellationToken = default)
    {
        var request = new DeleteObjectRequest
        {
            BucketName = _bucketName,
            Key = s3Key
        };

        await _s3Client.DeleteObjectAsync(request, cancellationToken);

        _logger.LogInformation("Deleted inbound email {S3Key}", s3Key);
    }
}
