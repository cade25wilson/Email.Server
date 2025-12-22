using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Email.Server.DTOs.Requests;

public class CreateApiKeyRequest
{
    [Required]
    [MaxLength(200)]
    [JsonPropertyName("name")]
    public required string Name { get; set; }

    [Required]
    [JsonPropertyName("domain_id")]
    public required Guid DomainId { get; set; }

    [Required]
    [MinLength(1)]
    [JsonPropertyName("scopes")]
    public required List<string> Scopes { get; set; }
}

/// <summary>
/// Available API key scopes
/// </summary>
public static class ApiKeyScopes
{
    public const string EmailsSend = "emails:send";
    public const string EmailsRead = "emails:read";
    public const string EmailsWrite = "emails:write";
    public const string DomainsRead = "domains:read";
    public const string DomainsWrite = "domains:write";
    public const string DomainsDelete = "domains:delete";
    public const string MessagesRead = "messages:read";
    public const string SmsSend = "sms:send";
    public const string SmsRead = "sms:read";
    public const string SmsWrite = "sms:write";
    public const string PushSend = "push:send";
    public const string PushRead = "push:read";
    public const string PushWrite = "push:write";

    public static readonly string[] All =
    [
        EmailsSend,
        EmailsRead,
        EmailsWrite,
        DomainsRead,
        DomainsWrite,
        DomainsDelete,
        MessagesRead,
        SmsSend,
        SmsRead,
        SmsWrite,
        PushSend,
        PushRead,
        PushWrite
    ];

    /// <summary>
    /// Full Access - all permissions
    /// </summary>
    public static readonly string[] FullAccess = All;

    /// <summary>
    /// Sending Only - send emails/SMS/push and view messages
    /// </summary>
    public static readonly string[] SendingOnly =
    [
        EmailsSend,
        EmailsRead,
        MessagesRead,
        SmsSend,
        SmsRead,
        PushSend,
        PushRead
    ];

    public static bool IsValid(string scope) => All.Contains(scope);
}
