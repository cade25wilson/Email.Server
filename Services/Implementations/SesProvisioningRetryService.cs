using Email.Server.Data;
using Email.Server.Models;
using Email.Server.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Email.Server.Services.Implementations
{
    public class SesProvisioningRetryService : BackgroundService, ISesProvisioningRetryService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<SesProvisioningRetryService> _logger;
        private readonly ILoggerFactory _loggerFactory;
        private readonly ISesClientFactory _sesClientFactory;
        private readonly TimeSpan _retryInterval = TimeSpan.FromMinutes(10);
        private readonly int _maxRetryAttempts = 5;

        public SesProvisioningRetryService(
            IServiceProvider serviceProvider,
            ILogger<SesProvisioningRetryService> logger,
            ILoggerFactory loggerFactory,
            ISesClientFactory sesClientFactory)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
            _loggerFactory = loggerFactory;
            _sesClientFactory = sesClientFactory;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("SES Provisioning Retry Service started");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await RetryFailedProvisionsAsync(stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error during SES provisioning retry cycle");
                }

                await Task.Delay(_retryInterval, stoppingToken);
            }

            _logger.LogInformation("SES Provisioning Retry Service stopped");
        }

        public async Task RetryFailedProvisionsAsync(CancellationToken cancellationToken = default)
        {
            using var scope = _serviceProvider.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            // Find regions with failed or pending provisioning
            var failedRegions = await context.SesRegions
                .Where(sr => sr.ProvisioningStatus == ProvisioningStatus.Failed ||
                             sr.ProvisioningStatus == ProvisioningStatus.Pending)
                .ToListAsync(cancellationToken);

            if (failedRegions.Count == 0)
            {
                _logger.LogDebug("No failed SES provisioning attempts to retry");
                return;
            }

            _logger.LogInformation("Found {Count} SES regions to retry provisioning", failedRegions.Count);

            foreach (var sesRegion in failedRegions)
            {
                if (cancellationToken.IsCancellationRequested)
                    break;

                try
                {
                    // Check if we've exceeded max retry attempts (approximate based on time)
                    var hoursSinceCreation = (DateTime.UtcNow - sesRegion.CreatedAtUtc).TotalHours;
                    var estimatedAttempts = (int)(hoursSinceCreation / (_retryInterval.TotalHours));

                    if (estimatedAttempts > _maxRetryAttempts)
                    {
                        _logger.LogWarning(
                            "SES region {RegionId} for tenant {TenantId} has exceeded max retry attempts. Skipping.",
                            sesRegion.Id, sesRegion.TenantId);
                        continue;
                    }

                    // Create region-specific SES service
                    var sesService = _sesClientFactory.CreateSesClientService(sesRegion.Region);

                    // Retry creating the AWS SES tenant
                    var response = await sesService.CreateSesTenantAsync(sesRegion.AwsSesTenantName!, cancellationToken);

                    // Update with AWS SES tenant metadata
                    sesRegion.AwsSesTenantId = response.TenantId;
                    sesRegion.AwsSesTenantArn = response.TenantArn;
                    sesRegion.SendingStatus = response.SendingStatus?.ToString();
                    sesRegion.SesTenantCreatedAt = response.CreatedTimestamp;
                    sesRegion.ProvisioningStatus = ProvisioningStatus.Provisioned;
                    sesRegion.ProvisioningErrorMessage = null;
                    sesRegion.LastStatusCheckUtc = DateTime.UtcNow;

                    await context.SaveChangesAsync(cancellationToken);

                    _logger.LogInformation(
                        "Successfully provisioned AWS SES tenant {TenantName} (ID: {TenantId}, ARN: {TenantArn}) in region {Region}",
                        sesRegion.AwsSesTenantName, response.TenantId, response.TenantArn, sesRegion.Region);
                }
                catch (Exception ex)
                {
                    // Update error message but keep status as Failed for next retry
                    sesRegion.ProvisioningErrorMessage = $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}] {ex.Message}";
                    sesRegion.LastStatusCheckUtc = DateTime.UtcNow;

                    await context.SaveChangesAsync(cancellationToken);

                    _logger.LogError(ex,
                        "Failed to provision AWS SES tenant {TenantName} in region {Region} for tenant {TenantId}. Will retry later.",
                        sesRegion.AwsSesTenantName, sesRegion.Region, sesRegion.TenantId);
                }
            }
        }
    }
}
