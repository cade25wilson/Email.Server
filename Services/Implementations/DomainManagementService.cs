using AutoMapper;
using Email.Server.Data;
using Email.Server.DTOs.Requests;
using Email.Server.DTOs.Responses;
using Email.Server.Models;
using Email.Server.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Email.Server.Services.Implementations;

public class DomainManagementService : IDomainManagementService
{
    private readonly ApplicationDbContext _context;
    private readonly ITenantContextService _tenantContext;
    private readonly ISesClientService _sesClient;
    private readonly IMapper _mapper;
    private readonly ILogger<DomainManagementService> _logger;
    private readonly IConfiguration _configuration;

    public DomainManagementService(
        ApplicationDbContext context,
        ITenantContextService tenantContext,
        ISesClientService sesClient,
        IMapper mapper,
        ILogger<DomainManagementService> logger,
        IConfiguration configuration)
    {
        _context = context;
        _tenantContext = tenantContext;
        _sesClient = sesClient;
        _mapper = mapper;
        _logger = logger;
        _configuration = configuration;
    }

    public async Task<DomainResponse> CreateDomainAsync(CreateDomainRequest request, CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContext.GetTenantId();

        // Check if domain already exists for this tenant
        var existingDomain = await _context.Domains
            .FirstOrDefaultAsync(d => d.TenantId == tenantId && d.Domain == request.Domain && d.Region == request.Region, cancellationToken);

        if (existingDomain != null)
        {
            throw new InvalidOperationException($"Domain {request.Domain} already exists in region {request.Region}");
        }

        // Get the SesRegion for this tenant and region
        var sesRegion = await _context.SesRegions
            .FirstOrDefaultAsync(sr => sr.TenantId == tenantId && sr.Region == request.Region, cancellationToken);

        if (sesRegion == null)
        {
            throw new InvalidOperationException($"Region {request.Region} is not enabled for this tenant");
        }

        // Check if a default ConfigSet exists for this SesRegion
        var existingConfigSet = await _context.ConfigSets
            .FirstOrDefaultAsync(cs => cs.SesRegionId == sesRegion.Id && cs.IsDefault, cancellationToken);

        using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            // If no default ConfigSet exists, create one in AWS and database
            if (existingConfigSet == null)
            {
                var configSetName = $"tenant-{tenantId}-{request.Region}";
                var awsAccountId = _configuration["AWS:AccountId"] ?? throw new InvalidOperationException("AWS:AccountId is not configured");

                _logger.LogInformation("Creating SES configuration set {ConfigSetName} for tenant {TenantId}", configSetName, tenantId);

                // Create configuration set in AWS SES (handle if already exists)
                try
                {
                    await _sesClient.CreateConfigurationSetAsync(configSetName, cancellationToken);
                }
                catch (Amazon.SimpleEmailV2.Model.AlreadyExistsException)
                {
                    _logger.LogInformation("Configuration set {ConfigSetName} already exists in AWS, skipping creation", configSetName);
                }

                // Associate configuration set to AWS SES tenant if tenant name exists
                if (!string.IsNullOrEmpty(sesRegion.AwsSesTenantName))
                {
                    var configSetArn = $"arn:aws:ses:{request.Region}:{awsAccountId}:configuration-set/{configSetName}";
                    await _sesClient.AssociateResourceToTenantAsync(sesRegion.AwsSesTenantName, configSetArn, cancellationToken);
                    _logger.LogInformation("Associated configuration set {ConfigSetName} to AWS SES tenant {AwsSesTenantName}",
                        configSetName, sesRegion.AwsSesTenantName);
                }

                // Create ConfigSet entity in database
                var configSet = new ConfigSets
                {
                    SesRegionId = sesRegion.Id,
                    Name = $"Default Configuration Set",
                    ConfigSetName = configSetName,
                    IsDefault = true,
                    CreatedAtUtc = DateTime.UtcNow
                };

                _context.ConfigSets.Add(configSet);
                await _context.SaveChangesAsync(cancellationToken);

                _logger.LogInformation("Created configuration set {ConfigSetName} for tenant {TenantId} in region {Region}",
                    configSetName, tenantId, request.Region);
            }

            // Create SES email identity
            var sesResponse = await _sesClient.CreateEmailIdentityAsync(request.Domain, cancellationToken);

            // Associate email identity to AWS SES tenant if tenant name exists
            if (!string.IsNullOrEmpty(sesRegion.AwsSesTenantName))
            {
                var awsAccountId = _configuration["AWS:AccountId"] ?? throw new InvalidOperationException("AWS:AccountId is not configured");
                var identityArn = $"arn:aws:ses:{request.Region}:{awsAccountId}:identity/{request.Domain}";
                await _sesClient.AssociateResourceToTenantAsync(sesRegion.AwsSesTenantName, identityArn, cancellationToken);
                _logger.LogInformation("Associated email identity {Domain} to AWS SES tenant {AwsSesTenantName}",
                    request.Domain, sesRegion.AwsSesTenantName);
            }

            // Create domain entity
            var domain = new Domains
            {
                TenantId = tenantId,
                Domain = request.Domain,
                Region = request.Region,
                VerificationStatus = 0, // Pending
                DkimMode = 1, // Easy DKIM
                DkimStatus = 0, // Pending
                MailFromStatus = 0, // Off
                IdentityArn = sesResponse.IdentityType?.ToString(),
                CreatedAtUtc = DateTime.UtcNow
            };

            _context.Domains.Add(domain);

            // Add DNS records from SES response
            if (sesResponse.DkimAttributes?.Tokens != null)
            {
                foreach (var token in sesResponse.DkimAttributes.Tokens)
                {
                    var dnsRecord = new DomainDnsRecords
                    {
                        DomainId = domain.Id,
                        RecordType = "CNAME",
                        Host = $"{token}._domainkey.{request.Domain}",
                        Value = $"{token}.dkim.amazonses.com",
                        Required = true,
                        Status = 0 // Unknown
                    };
                    _context.DomainDnsRecords.Add(dnsRecord);
                }
            }

            // Add DMARC TXT record
            var dmarcRecord = new DomainDnsRecords
            {
                DomainId = domain.Id,
                RecordType = "TXT",
                Host = $"_dmarc.{request.Domain}",
                Value = "v=DMARC1; p=none;",
                Required = false,
                Status = 0 // Unknown
            };
            _context.DomainDnsRecords.Add(dmarcRecord);

            // Add MX record for inbound email if region supports receiving
            var regionCatalog = await _context.RegionsCatalog
                .FirstOrDefaultAsync(r => r.Region == request.Region, cancellationToken);

            if (regionCatalog?.ReceiveSupported == true)
            {
                var mxRecord = new DomainDnsRecords
                {
                    DomainId = domain.Id,
                    RecordType = "MX",
                    Host = request.Domain,
                    Value = $"10 inbound-smtp.{request.Region}.amazonaws.com",
                    Required = false, // Optional - only needed if user wants to receive emails
                    Status = 0 // Unknown
                };
                _context.DomainDnsRecords.Add(mxRecord);
            }

            await _context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            _logger.LogInformation("Created domain {Domain} for tenant {TenantId}", request.Domain, tenantId);

            return await GetDomainAsync(domain.Id, cancellationToken);
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(cancellationToken);
            _logger.LogError(ex, "Error creating domain {Domain} for tenant {TenantId}. Transaction rolled back.", request.Domain, tenantId);
            throw;
        }
    }

    public async Task<DomainResponse> GetDomainAsync(Guid domainId, CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContext.GetTenantId();

        var domain = await _context.Domains
            .AsNoTracking()
            .Include(d => d.RegionCatalog)
            .FirstOrDefaultAsync(d => d.Id == domainId && d.TenantId == tenantId, cancellationToken);

        if (domain == null)
        {
            throw new KeyNotFoundException($"Domain {domainId} not found");
        }

        var dnsRecords = await _context.DomainDnsRecords
            .AsNoTracking()
            .Where(r => r.DomainId == domainId)
            .ToListAsync(cancellationToken);

        var response = _mapper.Map<DomainResponse>(domain);
        response.DnsRecords = _mapper.Map<List<DnsRecordResponse>>(dnsRecords);

        return response;
    }

    public async Task<IEnumerable<DomainResponse>> GetDomainsAsync(CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContext.GetTenantId();

        var domains = await _context.Domains
            .Where(d => d.TenantId == tenantId)
            .OrderByDescending(d => d.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        return _mapper.Map<List<DomainResponse>>(domains);
    }

    public async Task<DomainResponse> VerifyDomainAsync(Guid domainId, CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContext.GetTenantId();

        var domain = await _context.Domains
            .FirstOrDefaultAsync(d => d.Id == domainId && d.TenantId == tenantId, cancellationToken);

        if (domain == null)
        {
            throw new KeyNotFoundException($"Domain {domainId} not found");
        }

        // Get verification status from SES
        var sesResponse = await _sesClient.GetEmailIdentityAsync(domain.Domain, cancellationToken);

        // Update domain status
        domain.VerificationStatus = (sesResponse.VerifiedForSendingStatus ?? false) ? (byte)1 : (byte)0;

        if (sesResponse.DkimAttributes?.Status != null)
        {
            var dkimStatus = sesResponse.DkimAttributes.Status;
            domain.DkimStatus = dkimStatus == Amazon.SimpleEmailV2.DkimStatus.SUCCESS ? (byte)1 :
                               dkimStatus == Amazon.SimpleEmailV2.DkimStatus.FAILED ? (byte)2 :
                               (byte)0;
        }

        if (domain.VerificationStatus == 1 && domain.VerifiedAtUtc == null)
        {
            domain.VerifiedAtUtc = DateTime.UtcNow;
        }

        // Update DNS record statuses based on DKIM status
        var dnsRecords = await _context.DomainDnsRecords
            .Where(r => r.DomainId == domainId)
            .ToListAsync(cancellationToken);

        foreach (var record in dnsRecords)
        {
            if (record.RecordType == "CNAME" && record.Host.Contains("._domainkey."))
            {
                // DKIM CNAME records - status based on DKIM verification
                record.Status = domain.DkimStatus == 1 ? (byte)1 : // Verified
                               domain.DkimStatus == 2 ? (byte)2 : // Failed
                               (byte)0; // Pending/Unknown
            }
            else if (record.RecordType == "TXT" && record.Host.StartsWith("_dmarc."))
            {
                // DMARC record - we can't verify this from SES, leave as unknown unless domain is verified
                // If domain is fully verified, assume DMARC is also configured
                if (domain.VerificationStatus == 1 && domain.DkimStatus == 1)
                {
                    record.Status = (byte)1; // Verified (assumed)
                }
            }
        }

        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Updated verification status for domain {Domain}: Verified={Verified}, DKIM={Dkim}",
            domain.Domain, domain.VerificationStatus, domain.DkimStatus);

        return await GetDomainAsync(domainId, cancellationToken);
    }

    public async Task DeleteDomainAsync(Guid domainId, CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContext.GetTenantId();

        var domain = await _context.Domains
            .FirstOrDefaultAsync(d => d.Id == domainId && d.TenantId == tenantId, cancellationToken) ?? throw new KeyNotFoundException($"Domain {domainId} not found");

        // Get the SesRegion to find the AWS SES tenant name
        var sesRegion = await _context.SesRegions
            .FirstOrDefaultAsync(sr => sr.TenantId == tenantId && sr.Region == domain.Region, cancellationToken);

        // Disassociate from AWS SES tenant before deletion
        if (sesRegion != null && !string.IsNullOrEmpty(sesRegion.AwsSesTenantName))
        {
            try
            {
                var awsAccountId = _configuration["AWS:AccountId"] ?? throw new InvalidOperationException("AWS:AccountId is not configured");
                var identityArn = $"arn:aws:ses:{domain.Region}:{awsAccountId}:identity/{domain.Domain}";

                _logger.LogInformation("Disassociating email identity {Domain} from AWS SES tenant {AwsSesTenantName}",
                    domain.Domain, sesRegion.AwsSesTenantName);

                await _sesClient.DisassociateResourceFromTenantAsync(sesRegion.AwsSesTenantName, identityArn, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error disassociating domain from AWS SES tenant, continuing with deletion");
            }
        }

        // Delete from SES
        try
        {
            await _sesClient.DeleteEmailIdentityAsync(domain.Domain, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error deleting domain from SES, continuing with database deletion");
        }

        // Delete API keys and domain in a transaction
        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            await _context.ApiKeys
                .Where(a => a.DomainId == domainId)
                .ExecuteDeleteAsync(cancellationToken);

            _context.Domains.Remove(domain);
            await _context.SaveChangesAsync(cancellationToken);

            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }

        _logger.LogInformation("Deleted domain {Domain} for tenant {TenantId}", domain.Domain, tenantId);
    }

    public async Task<DomainResponse> EnableInboundAsync(Guid domainId, CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContext.GetTenantId();

        var domain = await _context.Domains
            .Include(d => d.RegionCatalog)
            .FirstOrDefaultAsync(d => d.Id == domainId && d.TenantId == tenantId, cancellationToken)
            ?? throw new KeyNotFoundException($"Domain {domainId} not found");

        // Check if domain is verified
        if (domain.VerificationStatus != 1)
        {
            throw new InvalidOperationException("Domain must be verified before enabling inbound email");
        }

        // Check if region supports receiving
        if (domain.RegionCatalog?.ReceiveSupported != true)
        {
            throw new InvalidOperationException($"Region {domain.Region} does not support receiving emails");
        }

        // Enable inbound
        domain.InboundEnabled = true;
        domain.InboundStatus = 1; // Pending - MX record needs to be configured

        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Enabled inbound email for domain {Domain}", domain.Domain);

        return await GetDomainAsync(domainId, cancellationToken);
    }

    public async Task<DomainResponse> DisableInboundAsync(Guid domainId, CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContext.GetTenantId();

        var domain = await _context.Domains
            .FirstOrDefaultAsync(d => d.Id == domainId && d.TenantId == tenantId, cancellationToken)
            ?? throw new KeyNotFoundException($"Domain {domainId} not found");

        // Disable inbound
        domain.InboundEnabled = false;
        domain.InboundStatus = 0; // Off

        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Disabled inbound email for domain {Domain}", domain.Domain);

        return await GetDomainAsync(domainId, cancellationToken);
    }
}
