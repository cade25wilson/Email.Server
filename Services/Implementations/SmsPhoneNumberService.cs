using Email.Server.Data;
using Email.Server.DTOs.Responses;
using Email.Server.Models;
using Email.Server.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Email.Server.Services.Implementations;

public class SmsPhoneNumberService : ISmsPhoneNumberService
{
    private readonly ApplicationDbContext _context;
    private readonly ITenantContextService _tenantContext;
    private readonly ILogger<SmsPhoneNumberService> _logger;

    public SmsPhoneNumberService(
        ApplicationDbContext context,
        ITenantContextService tenantContext,
        ILogger<SmsPhoneNumberService> logger)
    {
        _context = context;
        _tenantContext = tenantContext;
        _logger = logger;
    }

    public async Task<SmsPhoneNumberListResponse> ListPhoneNumbersAsync(CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContext.GetTenantId();

        var phoneNumbers = await _context.SmsPhoneNumbers
            .Where(p => p.TenantId == tenantId)
            .OrderByDescending(p => p.IsDefault)
            .ThenBy(p => p.CreatedAtUtc)
            .Select(p => MapToResponse(p))
            .ToListAsync(cancellationToken);

        return new SmsPhoneNumberListResponse
        {
            PhoneNumbers = phoneNumbers,
            Total = phoneNumbers.Count
        };
    }

    public async Task<SmsPhoneNumbers?> GetPhoneNumberAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContext.GetTenantId();
        return await _context.SmsPhoneNumbers
            .FirstOrDefaultAsync(p => p.Id == id && p.TenantId == tenantId, cancellationToken);
    }

    public async Task<SmsPhoneNumbers?> GetDefaultPhoneNumberAsync(CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContext.GetTenantId();

        // First try to get the default phone number
        var defaultPhone = await _context.SmsPhoneNumbers
            .FirstOrDefaultAsync(p => p.TenantId == tenantId && p.IsDefault && p.IsActive, cancellationToken);

        if (defaultPhone != null)
        {
            return defaultPhone;
        }

        // If no default, get the first active phone number
        return await _context.SmsPhoneNumbers
            .FirstOrDefaultAsync(p => p.TenantId == tenantId && p.IsActive, cancellationToken);
    }

    public async Task<bool> SetDefaultPhoneNumberAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContext.GetTenantId();

        // Verify the phone number exists and belongs to this tenant
        var phoneNumber = await _context.SmsPhoneNumbers
            .FirstOrDefaultAsync(p => p.Id == id && p.TenantId == tenantId, cancellationToken);

        if (phoneNumber == null)
        {
            return false;
        }

        // Clear existing default
        await _context.SmsPhoneNumbers
            .Where(p => p.TenantId == tenantId && p.IsDefault)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.IsDefault, false), cancellationToken);

        // Set new default
        phoneNumber.IsDefault = true;
        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Set default phone number to {PhoneNumber} for tenant {TenantId}",
            phoneNumber.PhoneNumber, tenantId);

        return true;
    }

    public async Task<SmsPhoneNumbers?> ValidatePhoneNumberAsync(string phoneNumber, CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContext.GetTenantId();

        return await _context.SmsPhoneNumbers
            .FirstOrDefaultAsync(p =>
                p.TenantId == tenantId &&
                p.PhoneNumber == phoneNumber &&
                p.IsActive, cancellationToken);
    }

    private static SmsPhoneNumberResponse MapToResponse(SmsPhoneNumbers phone)
    {
        return new SmsPhoneNumberResponse
        {
            Id = phone.Id,
            PhoneNumber = phone.PhoneNumber,
            NumberType = phone.NumberType.ToString(),
            Country = phone.Country,
            MonthlyFeeCents = phone.MonthlyFeeCents,
            IsDefault = phone.IsDefault,
            IsActive = phone.IsActive,
            ProvisionedAtUtc = phone.ProvisionedAtUtc,
            CreatedAtUtc = phone.CreatedAtUtc
        };
    }
}
