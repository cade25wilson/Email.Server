using System.Security.Cryptography;
using System.Text;
using Email.Server.Data;
using Email.Server.Models;
using Email.Server.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Email.Server.Services.Implementations;

public class ApiKeyService : IApiKeyService
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<ApiKeyService> _logger;

    private const string KeyPrefixStr = "sk_";
    private const int PrefixLength = 8;
    private const int RandomPartLength = 32;
    private const string AllowedChars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";

    public ApiKeyService(ApplicationDbContext context, ILogger<ApiKeyService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<CreateApiKeyResult> CreateAsync(Guid tenantId, Guid domainId, string name, IEnumerable<string> scopes, CancellationToken ct = default)
    {
        // Verify domain belongs to tenant
        var domain = await _context.Domains
            .FirstOrDefaultAsync(d => d.Id == domainId && d.TenantId == tenantId, ct)
            ?? throw new ArgumentException("Domain not found or does not belong to this tenant");

        // Generate unique prefix (retry if collision)
        string prefix;
        int attempts = 0;
        do
        {
            prefix = GenerateRandomString(PrefixLength);
            attempts++;
            if (attempts > 10)
            {
                throw new InvalidOperationException("Failed to generate unique API key prefix");
            }
        } while (await _context.ApiKeys.AnyAsync(k => k.KeyPrefix == prefix, ct));

        // Generate random part
        var randomPart = GenerateRandomString(RandomPartLength);

        // Full key: sk_<prefix>_<random>
        var fullKey = $"{KeyPrefixStr}{prefix}_{randomPart}";

        // Hash full key with SHA-256
        var keyHash = ComputeSha256Hash(fullKey);

        // Store scopes as comma-separated
        var scopesList = scopes.ToList();
        var scopesString = string.Join(",", scopesList);

        var apiKey = new ApiKeys
        {
            TenantId = tenantId,
            DomainId = domainId,
            Name = name,
            KeyPrefix = prefix,
            KeyHash = keyHash,
            Scopes = scopesString,
            CreatedAtUtc = DateTime.UtcNow
        };

        _context.ApiKeys.Add(apiKey);
        await _context.SaveChangesAsync(ct);

        _logger.LogInformation("Created API key {KeyId} for tenant {TenantId} domain {DomainId} with scopes: {Scopes}",
            apiKey.Id, tenantId, domainId, scopesString);

        return new CreateApiKeyResult
        {
            Id = apiKey.Id,
            Name = name,
            Key = fullKey,
            KeyPreview = $"{KeyPrefixStr}{prefix}_...",
            Scopes = scopesList,
            DomainId = domainId,
            DomainName = domain.Domain,
            CreatedAtUtc = apiKey.CreatedAtUtc
        };
    }

    public async Task<ApiKeyValidationResult?> ValidateAsync(string apiKey, CancellationToken ct = default)
    {
        // Parse: expect format sk_<prefix>_<random>
        if (string.IsNullOrWhiteSpace(apiKey) || !apiKey.StartsWith(KeyPrefixStr))
        {
            return null;
        }

        var withoutPrefix = apiKey.Substring(KeyPrefixStr.Length);
        var underscoreIndex = withoutPrefix.IndexOf('_');
        if (underscoreIndex < 0)
        {
            return null;
        }

        var prefix = withoutPrefix.Substring(0, underscoreIndex);

        // Lookup by prefix (unique index) with domain
        var storedKey = await _context.ApiKeys
            .Include(k => k.Domain)
            .FirstOrDefaultAsync(k => k.KeyPrefix == prefix && !k.IsRevoked, ct);

        if (storedKey == null)
        {
            return null;
        }

        // Compute hash and compare (constant-time)
        var computedHash = ComputeSha256Hash(apiKey);
        if (!CryptographicOperations.FixedTimeEquals(computedHash, storedKey.KeyHash))
        {
            return null;
        }

        // Parse scopes
        var scopes = string.IsNullOrEmpty(storedKey.Scopes)
            ? new List<string>()
            : storedKey.Scopes.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList();

        return new ApiKeyValidationResult
        {
            KeyId = storedKey.Id,
            TenantId = storedKey.TenantId,
            DomainId = storedKey.DomainId,
            DomainName = storedKey.Domain?.Domain ?? string.Empty,
            Scopes = scopes
        };
    }

    public async Task<IEnumerable<ApiKeyListItem>> GetAllAsync(Guid tenantId, CancellationToken ct = default)
    {
        var keys = await _context.ApiKeys
            .Include(k => k.Domain)
            .Where(k => k.TenantId == tenantId)
            .OrderByDescending(k => k.CreatedAtUtc)
            .ToListAsync(ct);

        return keys.Select(k => new ApiKeyListItem
        {
            Id = k.Id,
            Name = k.Name,
            KeyPreview = $"{KeyPrefixStr}{k.KeyPrefix}_...",
            Scopes = string.IsNullOrEmpty(k.Scopes)
                ? new List<string>()
                : k.Scopes.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList(),
            DomainId = k.DomainId,
            DomainName = k.Domain?.Domain ?? string.Empty,
            CreatedAtUtc = k.CreatedAtUtc,
            LastUsedAtUtc = k.LastUsedAtUtc,
            IsRevoked = k.IsRevoked
        });
    }

    public async Task<bool> RevokeAsync(Guid tenantId, Guid keyId, CancellationToken ct = default)
    {
        var key = await _context.ApiKeys
            .FirstOrDefaultAsync(k => k.Id == keyId && k.TenantId == tenantId, ct);

        if (key == null)
        {
            return false;
        }

        key.IsRevoked = true;
        await _context.SaveChangesAsync(ct);

        _logger.LogInformation("Revoked API key {KeyId} for tenant {TenantId}", keyId, tenantId);
        return true;
    }

    public async Task UpdateLastUsedAsync(Guid keyId, CancellationToken ct = default)
    {
        try
        {
            await _context.ApiKeys
                .Where(k => k.Id == keyId)
                .ExecuteUpdateAsync(s => s.SetProperty(k => k.LastUsedAtUtc, DateTime.UtcNow), ct);
        }
        catch (Exception ex)
        {
            // Fire-and-forget, don't fail the request
            _logger.LogWarning(ex, "Failed to update LastUsedAtUtc for API key {KeyId}", keyId);
        }
    }

    private static byte[] ComputeSha256Hash(string input)
    {
        return SHA256.HashData(Encoding.UTF8.GetBytes(input));
    }

    private static string GenerateRandomString(int length)
    {
        var bytes = RandomNumberGenerator.GetBytes(length);
        var chars = new char[length];
        for (int i = 0; i < length; i++)
        {
            chars[i] = AllowedChars[bytes[i] % AllowedChars.Length];
        }
        return new string(chars);
    }
}
