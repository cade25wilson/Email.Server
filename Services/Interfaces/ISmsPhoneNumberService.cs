using Email.Server.DTOs.Responses;
using Email.Server.Models;

namespace Email.Server.Services.Interfaces;

public interface ISmsPhoneNumberService
{
    /// <summary>
    /// Gets all phone numbers for the current tenant.
    /// </summary>
    Task<SmsPhoneNumberListResponse> ListPhoneNumbersAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a phone number by ID.
    /// </summary>
    Task<SmsPhoneNumbers?> GetPhoneNumberAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the default phone number for the tenant.
    /// </summary>
    Task<SmsPhoneNumbers?> GetDefaultPhoneNumberAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets the default phone number for the tenant.
    /// </summary>
    Task<bool> SetDefaultPhoneNumberAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates that a phone number belongs to the tenant and is active.
    /// </summary>
    Task<SmsPhoneNumbers?> ValidatePhoneNumberAsync(string phoneNumber, CancellationToken cancellationToken = default);
}
