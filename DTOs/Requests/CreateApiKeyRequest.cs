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

    public static readonly string[] All =
    [
        EmailsSend,
        EmailsRead,
        EmailsWrite,
        DomainsRead,
        DomainsWrite,
        DomainsDelete,
        MessagesRead
    ];

    /// <summary>
    /// Full Access - all permissions
    /// </summary>
    public static readonly string[] FullAccess = All;

    /// <summary>
    /// Sending Only - send emails and view messages
    /// </summary>
    public static readonly string[] SendingOnly =
    [
        EmailsSend,
        EmailsRead,
        MessagesRead
    ];

    public static bool IsValid(string scope) => All.Contains(scope);
}
