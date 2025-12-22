using Amazon.SimpleNotificationService;
using Amazon.SimpleNotificationService.Model;
using Email.Server.Configuration;
using Email.Server.Services.Interfaces;
using Microsoft.Extensions.Options;

namespace Email.Server.Services.Implementations;

/// <summary>
/// AWS SNS SMS client wrapper.
/// Uses SNS for transactional SMS (OTPs, notifications) via shared routes.
/// No 10DLC registration required - simpler but less throughput than dedicated numbers.
/// </summary>
public class AwsSmsClientService : ISmsClientService
{
    private readonly IAmazonSimpleNotificationService _snsClient;
    private readonly ILogger<AwsSmsClientService> _logger;
    private readonly AwsSmsSettings _settings;

    public AwsSmsClientService(
        IAmazonSimpleNotificationService snsClient,
        IOptions<AwsSmsSettings> settings,
        ILogger<AwsSmsClientService> logger)
    {
        _snsClient = snsClient;
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
