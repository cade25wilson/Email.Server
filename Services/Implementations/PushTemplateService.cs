using System.Text.Json;
using Email.Server.Data;
using Email.Server.Services.Interfaces;
using Email.Shared.DTOs.Requests;
using Email.Shared.DTOs.Responses;
using Email.Shared.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using IPushTemplateService = Email.Shared.Services.Interfaces.IPushTemplateService;

namespace Email.Server.Services.Implementations;

public class PushTemplateService : IPushTemplateService
{
    private readonly ApplicationDbContext _context;
    private readonly ITenantContextService _tenantContext;
    private readonly ILogger<PushTemplateService> _logger;

    public PushTemplateService(
        ApplicationDbContext context,
        ITenantContextService tenantContext,
        ILogger<PushTemplateService> logger)
    {
        _context = context;
        _tenantContext = tenantContext;
        _logger = logger;
    }

    public async Task<PushTemplateResponse> CreateTemplateAsync(CreatePushTemplateRequest request, CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContext.GetTenantId();

        // Check for duplicate name
        var exists = await _context.PushTemplates
            .AnyAsync(t => t.TenantId == tenantId && t.Name == request.Name, cancellationToken);

        if (exists)
        {
            throw new InvalidOperationException($"A template with name '{request.Name}' already exists");
        }

        var template = new PushTemplates
        {
            TenantId = tenantId,
            Name = request.Name,
            Title = request.Title,
            Body = request.Body,
            DefaultDataJson = request.DefaultData != null ? JsonSerializer.Serialize(request.DefaultData) : null,
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow
        };

        _context.PushTemplates.Add(template);
        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Created push template. Id: {Id}, Name: {Name}", template.Id, template.Name);

        return MapToResponse(template);
    }

    public async Task<PushTemplateResponse?> UpdateTemplateAsync(Guid id, UpdatePushTemplateRequest request, CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContext.GetTenantId();

        var template = await _context.PushTemplates
            .FirstOrDefaultAsync(t => t.Id == id && t.TenantId == tenantId, cancellationToken);

        if (template == null)
        {
            return null;
        }

        if (request.Name != null && request.Name != template.Name)
        {
            var exists = await _context.PushTemplates
                .AnyAsync(t => t.TenantId == tenantId && t.Name == request.Name && t.Id != id, cancellationToken);

            if (exists)
            {
                throw new InvalidOperationException($"A template with name '{request.Name}' already exists");
            }

            template.Name = request.Name;
        }

        if (request.Title != null) template.Title = request.Title;
        if (request.Body != null) template.Body = request.Body;
        if (request.IsActive.HasValue) template.IsActive = request.IsActive.Value;
        if (request.DefaultData != null) template.DefaultDataJson = JsonSerializer.Serialize(request.DefaultData);

        template.UpdatedAtUtc = DateTime.UtcNow;

        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Updated push template. Id: {Id}", template.Id);

        return MapToResponse(template);
    }

    public async Task<PushTemplates?> GetTemplateAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContext.GetTenantId();

        return await _context.PushTemplates
            .FirstOrDefaultAsync(t => t.Id == id && t.TenantId == tenantId, cancellationToken);
    }

    public async Task<PushTemplates?> GetTemplateByNameAsync(string name, CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContext.GetTenantId();

        return await _context.PushTemplates
            .FirstOrDefaultAsync(t => t.Name == name && t.TenantId == tenantId && t.IsActive, cancellationToken);
    }

    public async Task<PushTemplateListResponse> ListTemplatesAsync(int page = 1, int pageSize = 20, CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContext.GetTenantId();

        var query = _context.PushTemplates
            .Where(t => t.TenantId == tenantId)
            .OrderByDescending(t => t.CreatedAtUtc);

        var total = await query.CountAsync(cancellationToken);

        var templates = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return new PushTemplateListResponse
        {
            Templates = templates.Select(MapToResponse).ToList(),
            Total = total,
            Page = page,
            PageSize = pageSize
        };
    }

    public async Task<bool> DeleteTemplateAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContext.GetTenantId();

        var template = await _context.PushTemplates
            .FirstOrDefaultAsync(t => t.Id == id && t.TenantId == tenantId, cancellationToken);

        if (template == null)
        {
            return false;
        }

        _context.PushTemplates.Remove(template);
        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Deleted push template. Id: {Id}", id);

        return true;
    }

    public (string title, string body) RenderTemplate(string titleTemplate, string bodyTemplate, Dictionary<string, string>? variables)
    {
        if (variables == null || variables.Count == 0)
        {
            return (titleTemplate, bodyTemplate);
        }

        var title = titleTemplate;
        var body = bodyTemplate;

        foreach (var variable in variables)
        {
            var placeholder = $"{{{{{variable.Key}}}}}";
            title = title.Replace(placeholder, variable.Value);
            body = body.Replace(placeholder, variable.Value);
        }

        return (title, body);
    }

    private static PushTemplateResponse MapToResponse(PushTemplates template)
    {
        Dictionary<string, string>? defaultData = null;
        if (!string.IsNullOrEmpty(template.DefaultDataJson))
        {
            defaultData = JsonSerializer.Deserialize<Dictionary<string, string>>(template.DefaultDataJson);
        }

        return new PushTemplateResponse
        {
            Id = template.Id,
            Name = template.Name,
            Title = template.Title,
            Body = template.Body,
            DefaultData = defaultData,
            IsActive = template.IsActive,
            CreatedAtUtc = template.CreatedAtUtc,
            UpdatedAtUtc = template.UpdatedAtUtc
        };
    }
}
