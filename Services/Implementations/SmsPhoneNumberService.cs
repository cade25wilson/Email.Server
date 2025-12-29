using Amazon.PinpointSMSVoiceV2;
using Amazon.PinpointSMSVoiceV2.Model;
using Email.Server.Data;
using Email.Server.DTOs.Requests;
using Email.Server.DTOs.Responses;
using Email.Server.Models;
using Email.Server.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Email.Server.Services.Implementations;

public class SmsPhoneNumberService : ISmsPhoneNumberService
{
    private readonly ApplicationDbContext _context;
    private readonly ITenantContextService _tenantContext;
    private readonly IAmazonPinpointSMSVoiceV2 _pinpointClient;
    private readonly ISmsPoolService _poolService;
    private readonly ILogger<SmsPhoneNumberService> _logger;

    public SmsPhoneNumberService(
        ApplicationDbContext context,
        ITenantContextService tenantContext,
        IAmazonPinpointSMSVoiceV2 pinpointClient,
        ISmsPoolService poolService,
        ILogger<SmsPhoneNumberService> logger)
    {
        _context = context;
        _tenantContext = tenantContext;
        _pinpointClient = pinpointClient;
        _poolService = poolService;
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

    public async Task<SmsPhoneNumberResponse> ProvisionPhoneNumberAsync(ProvisionPhoneNumberRequest request, CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContext.GetTenantId();

        // Parse number type
        if (!Enum.TryParse<SmsNumberType>(request.NumberType, out var numberType))
        {
            throw new InvalidOperationException($"Invalid number type: {request.NumberType}");
        }

        // Check if tenant already has maximum number of phone numbers (limit to 5)
        var existingCount = await _context.SmsPhoneNumbers
            .CountAsync(p => p.TenantId == tenantId, cancellationToken);

        if (existingCount >= 5)
        {
            throw new InvalidOperationException("Maximum of 5 phone numbers per tenant. Contact support for additional numbers.");
        }

        try
        {
            // Request phone number from AWS Pinpoint SMS Voice V2
            var awsRequest = new RequestPhoneNumberRequest
            {
                IsoCountryCode = request.Country,
                MessageType = MessageType.TRANSACTIONAL,
                NumberCapabilities = [NumberCapability.SMS],
                NumberType = numberType switch
                {
                    SmsNumberType.TollFree => RequestableNumberType.TOLL_FREE,
                    SmsNumberType.LongCode => RequestableNumberType.TEN_DLC,
                    SmsNumberType.ShortCode => throw new InvalidOperationException("Short codes require manual provisioning. Contact support."),
                    _ => RequestableNumberType.TOLL_FREE
                }
            };

            var response = await _pinpointClient.RequestPhoneNumberAsync(awsRequest, cancellationToken);

            // Calculate monthly fee based on number type
            var monthlyFeeCents = numberType switch
            {
                SmsNumberType.TollFree => 200, // $2/month
                SmsNumberType.LongCode => 100, // $1/month
                _ => 200
            };

            // Check if this is the first phone number (make it default)
            var isFirstNumber = existingCount == 0;

            // Get or create tenant's pool
            var pool = await _poolService.GetOrCreatePoolAsync(cancellationToken);

            // Create phone number record
            var phoneNumber = new SmsPhoneNumbers
            {
                TenantId = tenantId,
                PhoneNumber = response.PhoneNumber,
                PhoneNumberArn = response.PhoneNumberArn,
                NumberType = numberType,
                Country = request.Country,
                MonthlyFeeCents = monthlyFeeCents,
                IsDefault = isFirstNumber,
                IsActive = true,
                ProvisionedAtUtc = DateTime.UtcNow,
                CreatedAtUtc = DateTime.UtcNow,
                PoolId = pool.Id
            };

            _context.SmsPhoneNumbers.Add(phoneNumber);
            await _context.SaveChangesAsync(cancellationToken);

            // Add number to AWS pool (creates pool in AWS if first number)
            await _poolService.AddNumberToPoolAsync(pool, response.PhoneNumberArn, cancellationToken);

            _logger.LogInformation(
                "Provisioned phone number {PhoneNumber} (ARN: {Arn}) for tenant {TenantId} in pool {PoolId}",
                response.PhoneNumber, response.PhoneNumberArn, tenantId, pool.Id);

            return MapToResponse(phoneNumber);
        }
        catch (ValidationException ex)
        {
            _logger.LogError(ex, "Validation error provisioning phone number for tenant {TenantId}", tenantId);
            throw new InvalidOperationException($"Invalid request: {ex.Message}");
        }
        catch (ServiceQuotaExceededException ex)
        {
            _logger.LogError(ex, "Service quota exceeded for tenant {TenantId}", tenantId);
            throw new InvalidOperationException("AWS service quota exceeded. Contact support to increase limits.");
        }
        catch (AccessDeniedException ex)
        {
            _logger.LogError(ex, "Access denied provisioning phone number for tenant {TenantId}", tenantId);
            throw new InvalidOperationException("Access denied. Contact support for assistance.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error provisioning phone number for tenant {TenantId}", tenantId);
            throw new InvalidOperationException($"Failed to provision phone number: {ex.Message}");
        }
    }

    public async Task<bool> ReleasePhoneNumberAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContext.GetTenantId();

        var phoneNumber = await _context.SmsPhoneNumbers
            .Include(p => p.Pool)
            .FirstOrDefaultAsync(p => p.Id == id && p.TenantId == tenantId, cancellationToken);

        if (phoneNumber == null)
        {
            return false;
        }

        // Don't allow releasing the last/default phone number
        var otherActiveNumbers = await _context.SmsPhoneNumbers
            .CountAsync(p => p.TenantId == tenantId && p.Id != id && p.IsActive, cancellationToken);

        if (otherActiveNumbers == 0)
        {
            throw new InvalidOperationException("Cannot release the only active phone number.");
        }

        try
        {
            // Remove from pool first (before AWS release)
            if (phoneNumber.Pool != null && !string.IsNullOrEmpty(phoneNumber.PhoneNumberArn))
            {
                await _poolService.RemoveNumberFromPoolAsync(phoneNumber.Pool, phoneNumber.PhoneNumberArn, cancellationToken);
            }

            // Release phone number in AWS
            if (!string.IsNullOrEmpty(phoneNumber.PhoneNumberArn))
            {
                var awsRequest = new ReleasePhoneNumberRequest
                {
                    PhoneNumberId = phoneNumber.PhoneNumberArn
                };

                await _pinpointClient.ReleasePhoneNumberAsync(awsRequest, cancellationToken);
            }

            // If this was the default, set another number as default
            if (phoneNumber.IsDefault)
            {
                var nextDefault = await _context.SmsPhoneNumbers
                    .FirstOrDefaultAsync(p => p.TenantId == tenantId && p.Id != id && p.IsActive, cancellationToken);

                if (nextDefault != null)
                {
                    nextDefault.IsDefault = true;
                }
            }

            // Remove the phone number from our database
            _context.SmsPhoneNumbers.Remove(phoneNumber);
            await _context.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Released phone number {PhoneNumber} for tenant {TenantId}",
                phoneNumber.PhoneNumber, tenantId);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error releasing phone number {PhoneNumberId} for tenant {TenantId}", id, tenantId);
            throw new InvalidOperationException($"Failed to release phone number: {ex.Message}");
        }
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
