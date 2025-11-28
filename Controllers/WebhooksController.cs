using System.Text.Json;
using Email.Server.DTOs.Sns;
using Email.Server.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace Email.Server.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
public class WebhooksController(
    ISesNotificationService sesNotificationService,
    IHttpClientFactory httpClientFactory,
    ILogger<WebhooksController> logger) : ControllerBase
{
    private readonly ISesNotificationService _sesNotificationService = sesNotificationService;
    private readonly IHttpClientFactory _httpClientFactory = httpClientFactory;
    private readonly ILogger<WebhooksController> _logger = logger;

    /// <summary>
    /// Receives SES notifications from AWS SNS
    /// </summary>
    [HttpPost("ses")]
    public async Task<IActionResult> ReceiveSesNotification(CancellationToken cancellationToken)
    {
        try
        {
            // Read the raw body
            using var reader = new StreamReader(Request.Body);
            var body = await reader.ReadToEndAsync(cancellationToken);

            _logger.LogInformation("Received SNS notification: {Body}", body);

            // Parse the SNS message wrapper
            var snsMessage = JsonSerializer.Deserialize<SnsMessage>(body);
            if (snsMessage == null)
            {
                _logger.LogWarning("Failed to parse SNS message");
                return BadRequest("Invalid SNS message format");
            }

            // Handle SNS subscription confirmation
            if (snsMessage.Type == "SubscriptionConfirmation")
            {
                return await HandleSubscriptionConfirmation(snsMessage, cancellationToken);
            }

            // Handle notification
            if (snsMessage.Type == "Notification")
            {
                return await HandleNotification(snsMessage, cancellationToken);
            }

            // Handle unsubscribe confirmation
            if (snsMessage.Type == "UnsubscribeConfirmation")
            {
                _logger.LogInformation("Received unsubscribe confirmation for topic: {TopicArn}", snsMessage.TopicArn);
                return Ok();
            }

            _logger.LogWarning("Unknown SNS message type: {Type}", snsMessage.Type);
            return BadRequest($"Unknown message type: {snsMessage.Type}");
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse SNS message");
            return BadRequest("Invalid JSON format");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing SNS notification");
            return StatusCode(500, "Internal server error");
        }
    }

    private async Task<IActionResult> HandleSubscriptionConfirmation(SnsMessage snsMessage, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(snsMessage.SubscribeUrl))
        {
            _logger.LogWarning("Subscription confirmation missing SubscribeURL");
            return BadRequest("Missing SubscribeURL");
        }

        _logger.LogInformation("Confirming SNS subscription for topic: {TopicArn}", snsMessage.TopicArn);

        try
        {
            // Call the SubscribeURL to confirm the subscription
            var httpClient = _httpClientFactory.CreateClient();
            var response = await httpClient.GetAsync(snsMessage.SubscribeUrl, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("Successfully confirmed SNS subscription for topic: {TopicArn}", snsMessage.TopicArn);
                return Ok("Subscription confirmed");
            }
            else
            {
                _logger.LogError("Failed to confirm SNS subscription. Status: {StatusCode}", response.StatusCode);
                return StatusCode(500, "Failed to confirm subscription");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error confirming SNS subscription");
            return StatusCode(500, "Error confirming subscription");
        }
    }

    private async Task<IActionResult> HandleNotification(SnsMessage snsMessage, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(snsMessage.Message))
        {
            _logger.LogWarning("Notification message is empty");
            return BadRequest("Empty notification message");
        }

        try
        {
            // Parse the SES notification from the Message field
            var sesNotification = JsonSerializer.Deserialize<SesNotification>(snsMessage.Message);
            if (sesNotification == null)
            {
                _logger.LogWarning("Failed to parse SES notification from message");
                return BadRequest("Invalid SES notification format");
            }

            _logger.LogInformation("Processing SES notification: Type={NotificationType}, MessageId={MessageId}",
                sesNotification.NotificationType, sesNotification.Mail.MessageId);

            // Process the notification
            await _sesNotificationService.ProcessNotificationAsync(sesNotification, cancellationToken);

            return Ok();
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse SES notification from SNS message");
            return BadRequest("Invalid SES notification JSON");
        }
    }
}
