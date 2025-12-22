using System.Text.Json;
using Email.Server.Data;
using Email.Server.Services.Interfaces;
using Email.Shared.DTOs.Requests;
using Email.Shared.DTOs.Responses;
using Email.Shared.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using IPushClientService = Email.Shared.Services.Interfaces.IPushClientService;
using IPushDeviceService = Email.Shared.Services.Interfaces.IPushDeviceService;
using DeviceQueryParams = Email.Shared.Services.Interfaces.DeviceQueryParams;

namespace Email.Server.Services.Implementations;

public class PushDeviceService : IPushDeviceService
{
    private readonly ApplicationDbContext _context;
    private readonly ITenantContextService _tenantContext;
    private readonly IPushClientService _pushClient;
    private readonly ILogger<PushDeviceService> _logger;

    public PushDeviceService(
        ApplicationDbContext context,
        ITenantContextService tenantContext,
        IPushClientService pushClient,
        ILogger<PushDeviceService> logger)
    {
        _context = context;
        _tenantContext = tenantContext;
        _pushClient = pushClient;
        _logger = logger;
    }

    public async Task<PushDeviceResponse> RegisterDeviceAsync(RegisterDeviceRequest request, CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContext.GetTenantId();

        // Get the credential
        var credential = await _context.PushCredentials
            .FirstOrDefaultAsync(c => c.Id == request.CredentialId && c.TenantId == tenantId && c.IsActive, cancellationToken);

        if (credential == null)
        {
            throw new InvalidOperationException("Push credential not found or inactive");
        }

        if (string.IsNullOrEmpty(credential.AwsApplicationArn))
        {
            throw new InvalidOperationException("Push credential is not properly configured");
        }

        // Check for existing device with same token
        var existingDevice = await _context.PushDeviceTokens
            .FirstOrDefaultAsync(d => d.TenantId == tenantId && d.CredentialId == request.CredentialId && d.Token == request.Token, cancellationToken);

        if (existingDevice != null)
        {
            // Update existing device
            existingDevice.ExternalUserId = request.ExternalUserId;
            existingDevice.MetadataJson = request.Metadata != null ? JsonSerializer.Serialize(request.Metadata) : null;
            existingDevice.IsActive = true;
            existingDevice.LastSeenAtUtc = DateTime.UtcNow;
            existingDevice.UnregisteredAtUtc = null;

            await _context.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Updated existing device token. Id: {Id}", existingDevice.Id);

            return MapToResponse(existingDevice);
        }

        // Create endpoint in AWS SNS
        var endpointResult = await _pushClient.CreateEndpointAsync(
            credential.AwsApplicationArn,
            request.Token,
            request.ExternalUserId,
            cancellationToken);

        if (!endpointResult.Success)
        {
            throw new InvalidOperationException($"Failed to register device: {endpointResult.Error}");
        }

        var device = new PushDeviceTokens
        {
            TenantId = tenantId,
            CredentialId = request.CredentialId,
            Token = request.Token,
            Platform = request.Platform,
            ExternalUserId = request.ExternalUserId,
            MetadataJson = request.Metadata != null ? JsonSerializer.Serialize(request.Metadata) : null,
            AwsEndpointArn = endpointResult.EndpointArn,
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow,
            LastSeenAtUtc = DateTime.UtcNow
        };

        _context.PushDeviceTokens.Add(device);
        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Registered new device. Id: {Id}, Platform: {Platform}", device.Id, device.Platform);

        return MapToResponse(device);
    }

    public async Task<PushDeviceResponse?> UpdateDeviceAsync(Guid id, UpdateDeviceRequest request, CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContext.GetTenantId();

        var device = await _context.PushDeviceTokens
            .FirstOrDefaultAsync(d => d.Id == id && d.TenantId == tenantId, cancellationToken);

        if (device == null)
        {
            return null;
        }

        // Handle token refresh
        if (!string.IsNullOrEmpty(request.Token) && request.Token != device.Token)
        {
            if (!string.IsNullOrEmpty(device.AwsEndpointArn))
            {
                await _pushClient.UpdateEndpointAsync(device.AwsEndpointArn, request.Token, null, cancellationToken);
            }
            device.Token = request.Token;
        }

        if (request.ExternalUserId != null) device.ExternalUserId = request.ExternalUserId;
        if (request.Metadata != null) device.MetadataJson = JsonSerializer.Serialize(request.Metadata);
        if (request.IsActive.HasValue)
        {
            device.IsActive = request.IsActive.Value;
            if (!string.IsNullOrEmpty(device.AwsEndpointArn))
            {
                await _pushClient.UpdateEndpointAsync(device.AwsEndpointArn, null, request.IsActive.Value, cancellationToken);
            }
        }

        device.LastSeenAtUtc = DateTime.UtcNow;

        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Updated device. Id: {Id}", device.Id);

        return MapToResponse(device);
    }

    public async Task<PushDeviceTokens?> GetDeviceAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContext.GetTenantId();

        return await _context.PushDeviceTokens
            .FirstOrDefaultAsync(d => d.Id == id && d.TenantId == tenantId, cancellationToken);
    }

    public async Task<PushDeviceListResponse> ListDevicesAsync(DeviceQueryParams query, CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContext.GetTenantId();

        var dbQuery = _context.PushDeviceTokens
            .Where(d => d.TenantId == tenantId);

        if (query.CredentialId.HasValue)
        {
            dbQuery = dbQuery.Where(d => d.CredentialId == query.CredentialId.Value);
        }

        if (!string.IsNullOrEmpty(query.ExternalUserId))
        {
            dbQuery = dbQuery.Where(d => d.ExternalUserId == query.ExternalUserId);
        }

        if (query.Platform.HasValue)
        {
            dbQuery = dbQuery.Where(d => d.Platform == query.Platform.Value);
        }

        if (query.IsActive.HasValue)
        {
            dbQuery = dbQuery.Where(d => d.IsActive == query.IsActive.Value);
        }

        var total = await dbQuery.CountAsync(cancellationToken);

        var devices = await dbQuery
            .OrderByDescending(d => d.CreatedAtUtc)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToListAsync(cancellationToken);

        return new PushDeviceListResponse
        {
            Devices = devices.Select(MapToResponse).ToList(),
            Total = total,
            Page = query.Page,
            PageSize = query.PageSize
        };
    }

    public async Task<List<PushDeviceTokens>> GetDevicesByExternalUserAsync(string externalUserId, CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContext.GetTenantId();

        return await _context.PushDeviceTokens
            .Where(d => d.TenantId == tenantId && d.ExternalUserId == externalUserId && d.IsActive)
            .ToListAsync(cancellationToken);
    }

    public async Task<bool> UnregisterDeviceAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContext.GetTenantId();

        var device = await _context.PushDeviceTokens
            .FirstOrDefaultAsync(d => d.Id == id && d.TenantId == tenantId, cancellationToken);

        if (device == null)
        {
            return false;
        }

        // Disable in AWS
        if (!string.IsNullOrEmpty(device.AwsEndpointArn))
        {
            await _pushClient.DeleteEndpointAsync(device.AwsEndpointArn, cancellationToken);
        }

        device.IsActive = false;
        device.UnregisteredAtUtc = DateTime.UtcNow;

        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Unregistered device. Id: {Id}", id);

        return true;
    }

    public async Task<bool> UnregisterByTokenAsync(string token, CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContext.GetTenantId();

        var device = await _context.PushDeviceTokens
            .FirstOrDefaultAsync(d => d.TenantId == tenantId && d.Token == token && d.IsActive, cancellationToken);

        if (device == null)
        {
            return false;
        }

        return await UnregisterDeviceAsync(device.Id, cancellationToken);
    }

    private static PushDeviceResponse MapToResponse(PushDeviceTokens device)
    {
        Dictionary<string, string>? metadata = null;
        if (!string.IsNullOrEmpty(device.MetadataJson))
        {
            metadata = JsonSerializer.Deserialize<Dictionary<string, string>>(device.MetadataJson);
        }

        return new PushDeviceResponse
        {
            Id = device.Id,
            CredentialId = device.CredentialId,
            Token = device.Token,
            Platform = device.Platform,
            ExternalUserId = device.ExternalUserId,
            Metadata = metadata,
            IsActive = device.IsActive,
            CreatedAtUtc = device.CreatedAtUtc,
            LastSeenAtUtc = device.LastSeenAtUtc
        };
    }
}
