using Email.Server.DTOs.Sns;

namespace Email.Server.Services.Interfaces;

public interface ISesNotificationService
{
    Task ProcessNotificationAsync(SesNotification notification, CancellationToken cancellationToken = default);
}
