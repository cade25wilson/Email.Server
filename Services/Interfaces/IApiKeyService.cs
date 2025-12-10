namespace Email.Server.Services.Interfaces;

public interface IApiKeyService
{
    Task<CreateApiKeyResult> CreateAsync(Guid tenantId, Guid domainId, string name, IEnumerable<string> scopes, CancellationToken ct = default);
    Task<ApiKeyValidationResult?> ValidateAsync(string apiKey, CancellationToken ct = default);
    Task<IEnumerable<ApiKeyListItem>> GetAllAsync(Guid tenantId, CancellationToken ct = default);
    Task<bool> RevokeAsync(Guid tenantId, Guid keyId, CancellationToken ct = default);
    Task UpdateLastUsedAsync(Guid keyId, CancellationToken ct = default);
}

public class CreateApiKeyResult
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public required string Key { get; set; }  // Full key - shown only once
    public required string KeyPreview { get; set; }  // e.g., "sk_a1b2c3d4_..."
    public required List<string> Scopes { get; set; }
    public Guid DomainId { get; set; }
    public required string DomainName { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

public class ApiKeyValidationResult
{
    public Guid KeyId { get; set; }
    public Guid TenantId { get; set; }
    public Guid? DomainId { get; set; }
    public string? DomainName { get; set; }
    public required List<string> Scopes { get; set; }
}

public class ApiKeyListItem
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public required string KeyPreview { get; set; }
    public required List<string> Scopes { get; set; }
    public Guid? DomainId { get; set; }
    public string? DomainName { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? LastUsedAtUtc { get; set; }
    public bool IsRevoked { get; set; }
}
