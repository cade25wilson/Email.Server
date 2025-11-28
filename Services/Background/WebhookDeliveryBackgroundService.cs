using Email.Server.Services.Interfaces;

namespace Email.Server.Services.Background;

public class WebhookDeliveryBackgroundService(
    IServiceProvider serviceProvider,
    ILogger<WebhookDeliveryBackgroundService> logger) : BackgroundService
{
    private readonly IServiceProvider _serviceProvider = serviceProvider;
    private readonly ILogger<WebhookDeliveryBackgroundService> _logger = logger;

    private const int ProcessingIntervalSeconds = 30;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Webhook delivery background service started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessPendingDeliveriesAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing webhook deliveries");
            }

            await Task.Delay(TimeSpan.FromSeconds(ProcessingIntervalSeconds), stoppingToken);
        }

        _logger.LogInformation("Webhook delivery background service stopped");
    }

    private async Task ProcessPendingDeliveriesAsync(CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var webhookService = scope.ServiceProvider.GetRequiredService<IWebhookDeliveryService>();

        await webhookService.ProcessPendingDeliveriesAsync(cancellationToken);
    }
}
