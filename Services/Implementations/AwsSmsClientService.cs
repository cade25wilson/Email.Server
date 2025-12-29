using Amazon.PinpointSMSVoiceV2;
using Amazon.PinpointSMSVoiceV2.Model;
using Amazon.SimpleNotificationService;
using Amazon.SimpleNotificationService.Model;
using Email.Server.Configuration;
using Email.Server.Services.Interfaces;
using Microsoft.Extensions.Options;

namespace Email.Server.Services.Implementations;

/// <summary>
/// AWS SMS client wrapper supporting both SNS (shared routes) and Pinpoint SMS Voice V2 (tenant pools).
/// - SNS: Simple transactional SMS via shared routes (no 10DLC required)
/// - Pinpoint: Tenant-isolated pools with failover and load distribution
/// </summary>
public class AwsSmsClientService : ISmsClientService
{
    private readonly IAmazonSimpleNotificationService _snsClient;
    private readonly IAmazonPinpointSMSVoiceV2 _pinpointClient;
    private readonly ILogger<AwsSmsClientService> _logger;
    private readonly AwsSmsSettings _settings;

    public AwsSmsClientService(
        IAmazonSimpleNotificationService snsClient,
        IAmazonPinpointSMSVoiceV2 pinpointClient,
        IOptions<AwsSmsSettings> settings,
        ILogger<AwsSmsClientService> logger)
    {
        _snsClient = snsClient;
        _pinpointClient = pinpointClient;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<SmsSendResult> SendSmsAsync(
        string toNumber,
        string body,
        bool isTransactional = true,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var request = new PublishRequest
            {
                PhoneNumber = toNumber,
                Message = body,
                MessageAttributes = new Dictionary<string, MessageAttributeValue>
                {
                    ["AWS.SNS.SMS.SMSType"] = new MessageAttributeValue
                    {
                        DataType = "String",
                        StringValue = isTransactional ? "Transactional" : "Promotional"
                    }
                }
            };

            // Add sender ID if configured (appears as the "from" on the message)
            if (!string.IsNullOrEmpty(_settings.SenderId))
            {
                request.MessageAttributes["AWS.SNS.SMS.SenderID"] = new MessageAttributeValue
                {
                    DataType = "String",
                    StringValue = _settings.SenderId
                };
            }

            var response = await _snsClient.PublishAsync(request, cancellationToken);

            _logger.LogInformation(
                "SMS sent via AWS SNS. MessageId: {MessageId}, To: {To}, Type: {Type}",
                response.MessageId, toNumber, isTransactional ? "Transactional" : "Promotional");

            return new SmsSendResult
            {
                Success = true,
                MessageId = response.MessageId,
                SegmentCount = CalculateSegmentCount(body)
            };
        }
        catch (InvalidParameterException ex)
        {
            _logger.LogError(ex, "Invalid parameter sending SMS to {To}", toNumber);
            return new SmsSendResult
            {
                Success = false,
                Error = $"Invalid parameter: {ex.Message}"
            };
        }
        catch (InvalidParameterValueException ex)
        {
            _logger.LogError(ex, "Invalid parameter value sending SMS to {To}", toNumber);
            return new SmsSendResult
            {
                Success = false,
                Error = $"Invalid phone number or message: {ex.Message}"
            };
        }
        catch (ThrottledException ex)
        {
            _logger.LogWarning(ex, "Throttled while sending SMS to {To}", toNumber);
            return new SmsSendResult
            {
                Success = false,
                Error = "Rate limit exceeded. Please try again later."
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send SMS to {To}", toNumber);
            return new SmsSendResult
            {
                Success = false,
                Error = ex.Message
            };
        }
    }

    public async Task<SmsSendResult> SendSmsViaPoolAsync(
        string poolArn,
        string toNumber,
        string body,
        bool isTransactional = true,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var request = new SendTextMessageRequest
            {
                DestinationPhoneNumber = toNumber,
                MessageBody = body,
                MessageType = isTransactional ? MessageType.TRANSACTIONAL : MessageType.PROMOTIONAL,
                OriginationIdentity = poolArn // AWS picks a number from the pool
            };

            var response = await _pinpointClient.SendTextMessageAsync(request, cancellationToken);

            _logger.LogInformation(
                "SMS sent via Pinpoint Pool. MessageId: {MessageId}, To: {To}, Pool: {Pool}, Type: {Type}",
                response.MessageId, toNumber, poolArn, isTransactional ? "Transactional" : "Promotional");

            return new SmsSendResult
            {
                Success = true,
                MessageId = response.MessageId,
                SegmentCount = CalculateSegmentCount(body)
            };
        }
        catch (Amazon.PinpointSMSVoiceV2.Model.ValidationException ex)
        {
            _logger.LogError(ex, "Validation error sending SMS to {To} via pool {Pool}", toNumber, poolArn);
            return new SmsSendResult
            {
                Success = false,
                Error = $"Invalid parameter: {ex.Message}"
            };
        }
        catch (Amazon.PinpointSMSVoiceV2.Model.ResourceNotFoundException ex)
        {
            _logger.LogError(ex, "Pool {Pool} not found when sending SMS to {To}", poolArn, toNumber);
            return new SmsSendResult
            {
                Success = false,
                Error = "Phone number pool not found. Please provision a phone number first."
            };
        }
        catch (Amazon.PinpointSMSVoiceV2.Model.ThrottlingException ex)
        {
            _logger.LogWarning(ex, "Throttled while sending SMS to {To} via pool {Pool}", toNumber, poolArn);
            return new SmsSendResult
            {
                Success = false,
                Error = "Rate limit exceeded. Please try again later."
            };
        }
        catch (Amazon.PinpointSMSVoiceV2.Model.AccessDeniedException ex)
        {
            _logger.LogError(ex, "Access denied sending SMS to {To} via pool {Pool}", toNumber, poolArn);
            return new SmsSendResult
            {
                Success = false,
                Error = "Access denied. Contact support for assistance."
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send SMS to {To} via pool {Pool}", toNumber, poolArn);
            return new SmsSendResult
            {
                Success = false,
                Error = ex.Message
            };
        }
    }

    public int CalculateSegmentCount(string body)
    {
        if (string.IsNullOrEmpty(body))
            return 0;

        // Check if message contains non-GSM characters (requires UCS-2 encoding)
        var isUnicode = body.Any(c => !IsGsmCharacter(c));

        if (isUnicode)
        {
            // UCS-2: 70 chars per segment, 67 if multipart
            return body.Length <= 70 ? 1 : (int)Math.Ceiling(body.Length / 67.0);
        }
        else
        {
            // GSM-7: 160 chars per segment, 153 if multipart
            return body.Length <= 160 ? 1 : (int)Math.Ceiling(body.Length / 153.0);
        }
    }

    private static bool IsGsmCharacter(char c)
    {
        // GSM 03.38 basic character set
        const string gsmChars = "@£$¥èéùìòÇ\nØø\rÅåΔ_ΦΓΛΩΠΨΣΘΞ ÆæßÉ !\"#¤%&'()*+,-./0123456789:;<=>?¡ABCDEFGHIJKLMNOPQRSTUVWXYZÄÖÑÜ§¿abcdefghijklmnopqrstuvwxyzäöñüà";

        // Extended GSM characters (each counts as 2 chars)
        const string gsmExtended = "^{}\\[~]|€";

        return gsmChars.Contains(c) || gsmExtended.Contains(c);
    }
}
