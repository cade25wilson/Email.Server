using Email.Server.Data;
using Email.Server.DTOs.Requests;
using Email.Server.DTOs.Responses;
using Email.Server.Models;
using Email.Server.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Email.Server.Services.Implementations;

public class SmsTemplateService : ISmsTemplateService
{
    private readonly ApplicationDbContext _context;
    private readonly ITenantContextService _tenantContext;
    private readonly ILogger<SmsTemplateService> _logger;

    public SmsTemplateService(
        ApplicationDbContext context,
        ITenantContextService tenantContext,
        ILogger<SmsTemplateService> logger)
    {
        _context = context;
        _tenantContext = tenantContext;
        _logger = logger;
    }

    public async Task<SmsTemplateResponse> CreateTemplateAsync(
        CreateSmsTemplateRequest request,
        CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContext.GetTenantId();

        // Check for duplicate name
        var exists = await _context.SmsTemplates
            .AnyAsync(t => t.TenantId == tenantId && t.Name == request.Name, cancellationToken);

        if (exists)
        {
            throw new InvalidOperationException($"A template with name '{request.Name}' already exists");
        }

        var template = new SmsTemplates
        {
            TenantId = tenantId,
            Name = request.Name,
            Body = request.Body,
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow
        };

        _context.SmsTemplates.Add(template);
        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Created SMS template '{Name}' for tenant {TenantId}", request.Name, tenantId);

        return MapToResponse(template);
    }

    public async Task<SmsTemplateResponse?> UpdateTemplateAsync(
        Guid id,
        UpdateSmsTemplateRequest request,
        CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContext.GetTenantId();

        var template = await _context.SmsTemplates
            .FirstOrDefaultAsync(t => t.Id == id && t.TenantId == tenantId, cancellationToken);

        if (template == null)
        {
            return null;
        }

        // Check for duplicate name if name is being changed
        if (!string.IsNullOrEmpty(request.Name) && request.Name != template.Name)
        {
            var exists = await _context.SmsTemplates
                .AnyAsync(t => t.TenantId == tenantId && t.Name == request.Name && t.Id != id, cancellationToken);

            if (exists)
            {
                throw new InvalidOperationException($"A template with name '{request.Name}' already exists");
            }

            template.Name = request.Name;
        }

        if (!string.IsNullOrEmpty(request.Body))
        {
            template.Body = request.Body;
        }

        if (request.IsActive.HasValue)
        {
            template.IsActive = request.IsActive.Value;
        }

        template.UpdatedAtUtc = DateTime.UtcNow;

        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Updated SMS template {Id} for tenant {TenantId}", id, tenantId);

        return MapToResponse(template);
    }

    public async Task<SmsTemplates?> GetTemplateAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContext.GetTenantId();
        return await _context.SmsTemplates
            .FirstOrDefaultAsync(t => t.Id == id && t.TenantId == tenantId, cancellationToken);
    }

    public async Task<SmsTemplates?> GetTemplateByNameAsync(string name, CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContext.GetTenantId();
        return await _context.SmsTemplates
            .FirstOrDefaultAsync(t => t.Name == name && t.TenantId == tenantId, cancellationToken);
    }

    public async Task<SmsTemplateListResponse> ListTemplatesAsync(
        int page = 1,
        int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContext.GetTenantId();

        var query = _context.SmsTemplates
            .Where(t => t.TenantId == tenantId);

        var total = await query.CountAsync(cancellationToken);

        var templates = await query
            .OrderByDescending(t => t.CreatedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(t => MapToResponse(t))
            .ToListAsync(cancellationToken);

        return new SmsTemplateListResponse
        {
            Templates = templates,
            Total = total,
            Page = page,
            PageSize = pageSize
        };
    }

    public async Task<bool> DeleteTemplateAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContext.GetTenantId();

        var template = await _context.SmsTemplates
            .FirstOrDefaultAsync(t => t.Id == id && t.TenantId == tenantId, cancellationToken);

        if (template == null)
        {
            return false;
        }

        _context.SmsTemplates.Remove(template);
        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Deleted SMS template {Id} for tenant {TenantId}", id, tenantId);

        return true;
    }

    public string RenderTemplate(string templateBody, Dictionary<string, string>? variables)
    {
        if (variables == null || variables.Count == 0)
        {
            return templateBody;
        }

        var result = templateBody;
        foreach (var variable in variables)
        {
            result = result.Replace($"{{{{{variable.Key}}}}}", variable.Value);
        }

        return result;
    }

    private static SmsTemplateResponse MapToResponse(SmsTemplates template)
    {
        return new SmsTemplateResponse
        {
            Id = template.Id,
            Name = template.Name,
            Body = template.Body,
            IsActive = template.IsActive,
            CreatedAtUtc = template.CreatedAtUtc,
            UpdatedAtUtc = template.UpdatedAtUtc
        };
    }
}
