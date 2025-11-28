using Amazon;
using Amazon.Runtime;
using Amazon.Runtime.CredentialManagement;
using Amazon.SimpleEmailV2;
using Email.Server.Services.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Email.Server.Services.Implementations;

public interface ISesClientFactory
{
    AmazonSimpleEmailServiceV2Client CreateClient(string region);
    ISesClientService CreateSesClientService(string region);
}

public class SesClientFactory(IConfiguration configuration, ILogger<SesClientFactory> logger, ILoggerFactory loggerFactory) : ISesClientFactory
{
    private readonly IConfiguration _configuration = configuration;
    private readonly ILogger<SesClientFactory> _logger = logger;
    private readonly ILoggerFactory _loggerFactory = loggerFactory;

    public AmazonSimpleEmailServiceV2Client CreateClient(string region)
    {
        var regionEndpoint = RegionEndpoint.GetBySystemName(region);

        // 1. Try explicit access key from configuration (for Azure App Service)
        var accessKeyId = _configuration["AWS:AccessKeyId"];
        var secretAccessKey = _configuration["AWS:SecretAccessKey"];

        if (!string.IsNullOrEmpty(accessKeyId) && !string.IsNullOrEmpty(secretAccessKey))
        {
            _logger.LogDebug("Creating SES client for region {Region} using explicit credentials", region);
            var credentials = new BasicAWSCredentials(accessKeyId, secretAccessKey);
            return new AmazonSimpleEmailServiceV2Client(credentials, regionEndpoint);
        }

        // 2. Try AWS profile (for local development)
        var profileName = _configuration["AWS:Profile"];
        if (!string.IsNullOrEmpty(profileName))
        {
            var chain = new CredentialProfileStoreChain();
            if (chain.TryGetAWSCredentials(profileName, out var profileCredentials))
            {
                _logger.LogDebug("Creating SES client for region {Region} using profile {Profile}", region, profileName);
                return new AmazonSimpleEmailServiceV2Client(profileCredentials, regionEndpoint);
            }
        }

        // 3. Fall back to default credential chain (IAM roles, environment variables, etc.)
        _logger.LogDebug("Creating SES client for region {Region} using default credential chain", region);
        return new AmazonSimpleEmailServiceV2Client(regionEndpoint);
    }

    public ISesClientService CreateSesClientService(string region)
    {
        var client = CreateClient(region);
        return new SesClientService(client, _loggerFactory.CreateLogger<SesClientService>());
    }
}
