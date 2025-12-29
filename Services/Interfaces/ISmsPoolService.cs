using Email.Server.Models;

namespace Email.Server.Services.Interfaces;

public interface ISmsPoolService
{
    /// <summary>
    /// Gets the tenant's pool, creating a database record if it doesn't exist.
    /// Does not create the AWS pool - use EnsurePoolSetupAsync for that.
    /// </summary>
    Task<SmsPools> GetOrCreatePoolAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the tenant's pool if it exists.
    /// </summary>
    Task<SmsPools?> GetPoolAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Ensures the pool is set up in AWS with all active phone numbers.
    /// Call this before sending SMS to lazily initialize the AWS pool.
    /// </summary>
    Task<bool> EnsurePoolSetupAsync(SmsPools pool, CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds a phone number to the tenant's pool in AWS.
    /// </summary>
    Task AddNumberToPoolAsync(SmsPools pool, string phoneNumberArn, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a phone number from the pool in AWS.
    /// </summary>
    Task RemoveNumberFromPoolAsync(SmsPools pool, string phoneNumberArn, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes the pool from AWS and the database.
    /// </summary>
    Task DeletePoolAsync(Guid poolId, CancellationToken cancellationToken = default);
}
