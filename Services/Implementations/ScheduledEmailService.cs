using Email.Server.Data;
using Email.Server.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Email.Server.Services.Implementations;

public class ScheduledEmailService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ScheduledEmailService> _logger;
    private readonly TimeSpan _checkInterval = TimeSpan.FromSeconds(30);

    public ScheduledEmailService(
        IServiceProvider serviceProvider,
        ILogger<ScheduledEmailService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Scheduled Email Service started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessScheduledEmailsAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during scheduled email processing cycle");
            }

            await Task.Delay(_checkInterval, stoppingToken);
        }

        _logger.LogInformation("Scheduled Email Service stopped");
    }

    private async Task ProcessScheduledEmailsAsync(CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var emailService = scope.ServiceProvider.GetRequiredService<IEmailSendingService>();

        // Find scheduled messages that are due to be sent
        var now = DateTime.UtcNow;
        var dueMessages = await context.Messages
            .Where(m => m.Status == 4 && m.ScheduledAtUtc <= now) // Status 4 = Scheduled
            .OrderBy(m => m.ScheduledAtUtc)
            .Take(50) // Process in batches
            .Select(m => m.Id)
            .ToListAsync(cancellationToken);

        if (dueMessages.Count == 0)
        {
            return;
        }

        _logger.LogInformation("Found {Count} scheduled emails due for sending", dueMessages.Count);

        foreach (var messageId in dueMessages)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            try
            {
                var result = await emailService.SendScheduledMessageAsync(messageId, cancellationToken);

                if (result.Status == 1) // Sent
                {
                    _logger.LogInformation("Scheduled email {MessageId} sent successfully", messageId);
                }
                else
                {
                    _logger.LogWarning("Scheduled email {MessageId} failed: {Error}", messageId, result.Error);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process scheduled email {MessageId}", messageId);
            }
        }
    }
}
