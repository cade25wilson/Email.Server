using System.Text.Json;
using Amazon.SimpleNotificationService;
using Amazon.SimpleNotificationService.Model;
using Email.Shared.Configuration;
using Email.Shared.Models;
using Email.Shared.Services.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Email.Server.Services.Implementations;

/// <summary>
/// AWS SNS client wrapper for push notification operations.
/// </summary>
public class AwsPushClientService : IPushClientService
{
    private readonly IAmazonSimpleNotificationService _snsClient;
    private readonly ILogger<AwsPushClientService> _logger;
    private readonly AwsPushSettings _settings;

    public AwsPushClientService(
        IAmazonSimpleNotificationService snsClient,
        IOptions<AwsPushSettings> settings,
        ILogger<AwsPushClientService> logger)
    {
        _snsClient = snsClient;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<CreateApplicationResult> CreatePlatformApplicationAsync(
        PushPlatform platform,
        string applicationId,
        byte[] credentials,
        string? keyId = null,
        string? teamId = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var platformName = platform == PushPlatform.Apns ? "APNS" : "GCM";
            var attributes = new Dictionary<string, string>();

            if (platform == PushPlatform.Apns)
            {
                // APNs uses token-based auth (.p8 key)
                var privateKey = System.Text.Encoding.UTF8.GetString(credentials);
                attributes["PlatformPrincipal"] = keyId ?? throw new ArgumentException("KeyId is required for APNs");
                attributes["PlatformCredential"] = privateKey;
                attributes["ApplePlatformTeamID"] = teamId ?? throw new ArgumentException("TeamId is required for APNs");
                attributes["ApplePlatformBundleID"] = applicationId;
            }
            else
            {
                // FCM uses service account JSON
                var serviceAccountJson = System.Text.Encoding.UTF8.GetString(credentials);
                attributes["PlatformCredential"] = serviceAccountJson;
            }

            var request = new CreatePlatformApplicationRequest
            {
                Name = $"sendbase-{applicationId.Replace(".", "-")}",
                Platform = platformName,
                Attributes = attributes
            };

            var response = await _snsClient.CreatePlatformApplicationAsync(request, cancellationToken);

            _logger.LogInformation(
                "Created SNS platform application. ARN: {Arn}, Platform: {Platform}, AppId: {AppId}",
                response.PlatformApplicationArn, platformName, applicationId);

            return new CreateApplicationResult
            {
                Success = true,
                ApplicationArn = response.PlatformApplicationArn
            };
        }
        catch (InvalidParameterException ex)
        {
            _logger.LogError(ex, "Invalid credentials for platform application. Platform: {Platform}", platform);
            return new CreateApplicationResult
            {
                Success = false,
                Error = $"Invalid credentials: {ex.Message}"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create platform application. Platform: {Platform}", platform);
            return new CreateApplicationResult
            {
                Success = false,
                Error = ex.Message
            };
        }
    }

    public async Task<bool> DeletePlatformApplicationAsync(string applicationArn, CancellationToken cancellationToken = default)
    {
        try
        {
            await _snsClient.DeletePlatformApplicationAsync(
                new DeletePlatformApplicationRequest { PlatformApplicationArn = applicationArn },
                cancellationToken);

            _logger.LogInformation("Deleted SNS platform application. ARN: {Arn}", applicationArn);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete platform application. ARN: {Arn}", applicationArn);
            return false;
        }
    }

    public async Task<bool> ValidateApplicationAsync(string applicationArn, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _snsClient.GetPlatformApplicationAttributesAsync(
                new GetPlatformApplicationAttributesRequest { PlatformApplicationArn = applicationArn },
                cancellationToken);

            // Check if the application has enabled status
            return response.Attributes.TryGetValue("Enabled", out var enabled) &&
                   enabled.Equals("true", StringComparison.OrdinalIgnoreCase);
        }
        catch (NotFoundException)
        {
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to validate platform application. ARN: {Arn}", applicationArn);
            return false;
        }
    }

    public async Task<CreateEndpointResult> CreateEndpointAsync(
        string applicationArn,
        string deviceToken,
        string? customUserData = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var request = new CreatePlatformEndpointRequest
            {
                PlatformApplicationArn = applicationArn,
                Token = deviceToken
            };

            if (!string.IsNullOrEmpty(customUserData))
            {
                request.CustomUserData = customUserData;
            }

            var response = await _snsClient.CreatePlatformEndpointAsync(request, cancellationToken);

            _logger.LogInformation(
                "Created SNS endpoint. ARN: {Arn}, AppArn: {AppArn}",
                response.EndpointArn, applicationArn);

            return new CreateEndpointResult
            {
                Success = true,
                EndpointArn = response.EndpointArn
            };
        }
        catch (InvalidParameterException ex)
        {
            _logger.LogError(ex, "Invalid device token for endpoint creation");
            return new CreateEndpointResult
            {
                Success = false,
                Error = $"Invalid device token: {ex.Message}"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create endpoint. AppArn: {AppArn}", applicationArn);
            return new CreateEndpointResult
            {
                Success = false,
                Error = ex.Message
            };
        }
    }

    public async Task<bool> DeleteEndpointAsync(string endpointArn, CancellationToken cancellationToken = default)
    {
        try
        {
            await _snsClient.DeleteEndpointAsync(
                new DeleteEndpointRequest { EndpointArn = endpointArn },
                cancellationToken);

            _logger.LogInformation("Deleted SNS endpoint. ARN: {Arn}", endpointArn);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete endpoint. ARN: {Arn}", endpointArn);
            return false;
        }
    }

    public async Task<bool> UpdateEndpointAsync(
        string endpointArn,
        string? newToken = null,
        bool? enabled = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var attributes = new Dictionary<string, string>();

            if (!string.IsNullOrEmpty(newToken))
            {
                attributes["Token"] = newToken;
            }

            if (enabled.HasValue)
            {
                attributes["Enabled"] = enabled.Value.ToString().ToLower();
            }

            if (attributes.Count == 0)
            {
                return true; // Nothing to update
            }

            await _snsClient.SetEndpointAttributesAsync(
                new SetEndpointAttributesRequest
                {
                    EndpointArn = endpointArn,
                    Attributes = attributes
                },
                cancellationToken);

            _logger.LogInformation("Updated SNS endpoint. ARN: {Arn}", endpointArn);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update endpoint. ARN: {Arn}", endpointArn);
            return false;
        }
    }

    public async Task<PushSendResult> SendPushAsync(
        string endpointArn,
        PushPlatform platform,
        string title,
        string body,
        Dictionary<string, string>? data = null,
        PlatformPushOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Build platform-specific message payload
            var message = BuildPlatformMessage(platform, title, body, data, options);

            var request = new PublishRequest
            {
                TargetArn = endpointArn,
                Message = message,
                MessageStructure = "json" // Required for platform-specific messages
            };

            var response = await _snsClient.PublishAsync(request, cancellationToken);

            _logger.LogInformation(
                "Sent push notification. MessageId: {MessageId}, Endpoint: {Endpoint}",
                response.MessageId, endpointArn);

            return new PushSendResult
            {
                Success = true,
                MessageId = response.MessageId
            };
        }
        catch (EndpointDisabledException)
        {
            _logger.LogWarning("Endpoint is disabled. ARN: {Arn}", endpointArn);
            return new PushSendResult
            {
                Success = false,
                Error = "Device endpoint is disabled (token may be invalid)"
            };
        }
        catch (InvalidParameterException ex)
        {
            _logger.LogError(ex, "Invalid push notification parameters");
            return new PushSendResult
            {
                Success = false,
                Error = $"Invalid parameters: {ex.Message}"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send push notification. Endpoint: {Endpoint}", endpointArn);
            return new PushSendResult
            {
                Success = false,
                Error = ex.Message
            };
        }
    }

    private static string BuildPlatformMessage(
        PushPlatform platform,
        string title,
        string body,
        Dictionary<string, string>? data,
        PlatformPushOptions? options)
    {
        var messageDict = new Dictionary<string, object>();

        if (platform == PushPlatform.Apns)
        {
            var apnsPayload = new Dictionary<string, object>
            {
                ["aps"] = new Dictionary<string, object>
                {
                    ["alert"] = new Dictionary<string, string>
                    {
                        ["title"] = title,
                        ["body"] = body
                    },
                    ["sound"] = options?.ApnsSound ?? "default"
                }
            };

            if (options?.ApnsBadge.HasValue == true)
            {
                ((Dictionary<string, object>)apnsPayload["aps"])["badge"] = options.ApnsBadge.Value;
            }

            // Add custom data
            if (data != null)
            {
                foreach (var kvp in data)
                {
                    apnsPayload[kvp.Key] = kvp.Value;
                }
            }

            messageDict["APNS"] = JsonSerializer.Serialize(apnsPayload);
            messageDict["APNS_SANDBOX"] = JsonSerializer.Serialize(apnsPayload);
        }
        else // FCM
        {
            var fcmPayload = new Dictionary<string, object>
            {
                ["notification"] = new Dictionary<string, string>
                {
                    ["title"] = title,
                    ["body"] = body
                }
            };

            if (data != null && data.Count > 0)
            {
                fcmPayload["data"] = data;
            }

            if (options?.FcmPriority != null)
            {
                fcmPayload["priority"] = options.FcmPriority;
            }

            if (options?.FcmCollapseKey != null)
            {
                fcmPayload["collapse_key"] = options.FcmCollapseKey;
            }

            if (options?.FcmTtlSeconds.HasValue == true)
            {
                fcmPayload["time_to_live"] = options.FcmTtlSeconds.Value;
            }

            messageDict["GCM"] = JsonSerializer.Serialize(fcmPayload);
        }

        // Add default message for non-mobile platforms
        messageDict["default"] = body;

        return JsonSerializer.Serialize(messageDict);
    }
}
