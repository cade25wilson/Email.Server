using System.Text.RegularExpressions;
using Email.Server.Data;
using Email.Server.DTOs.Requests;
using Email.Server.DTOs.Responses;
using Email.Server.Models;
using Email.Server.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Email.Server.Services.Implementations;

public partial class TemplateService : ITemplateService
{
    private readonly ApplicationDbContext _context;
    private readonly ITenantContextService _tenantContext;
    private readonly ILogger<TemplateService> _logger;

    // Regex to find {{variable_name}} patterns
    [GeneratedRegex(@"\{\{(\w+)\}\}", RegexOptions.Compiled)]
    private static partial Regex VariablePattern();

    public TemplateService(
        ApplicationDbContext context,
        ITenantContextService tenantContext,
        ILogger<TemplateService> logger)
    {
        _context = context;
        _tenantContext = tenantContext;
        _logger = logger;
    }

    public async Task<TemplateResponse> CreateTemplateAsync(CreateTemplateRequest request, CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContext.GetTenantId();

        // Check if template with same name already exists
        var existingTemplate = await _context.Templates
            .FirstOrDefaultAsync(t => t.TenantId == tenantId && t.Name == request.Name, cancellationToken);

        if (existingTemplate != null)
        {
            throw new InvalidOperationException($"A template with the name '{request.Name}' already exists");
        }

        var template = new Templates
        {
            TenantId = tenantId,
            Name = request.Name,
            Subject = request.Subject,
            HtmlBody = request.HtmlBody,
            TextBody = request.TextBody,
            Version = 1,
            CreatedAtUtc = DateTime.UtcNow
        };

        _context.Templates.Add(template);
        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Template '{TemplateName}' created for tenant {TenantId}", request.Name, tenantId);

        return MapToResponse(template);
    }

    public async Task<TemplateResponse> GetTemplateAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContext.GetTenantId();

        var template = await _context.Templates
            .FirstOrDefaultAsync(t => t.Id == id && t.TenantId == tenantId, cancellationToken);

        if (template == null)
        {
            throw new KeyNotFoundException($"Template with ID {id} not found");
        }

        return MapToResponse(template);
    }

    public async Task<List<TemplateListResponse>> GetTemplatesAsync(CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContext.GetTenantId();

        var templates = await _context.Templates
            .Where(t => t.TenantId == tenantId)
            .OrderByDescending(t => t.CreatedAtUtc)
            .Select(t => new TemplateListResponse
            {
                Id = t.Id,
                Name = t.Name,
                Version = t.Version,
                Subject = t.Subject,
                CreatedAtUtc = t.CreatedAtUtc
            })
            .ToListAsync(cancellationToken);

        return templates;
    }

    public async Task<TemplateResponse> UpdateTemplateAsync(Guid id, UpdateTemplateRequest request, CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContext.GetTenantId();

        var template = await _context.Templates
            .FirstOrDefaultAsync(t => t.Id == id && t.TenantId == tenantId, cancellationToken);

        if (template == null)
        {
            throw new KeyNotFoundException($"Template with ID {id} not found");
        }

        // Check for name conflict if name is being changed
        if (!string.IsNullOrEmpty(request.Name) && request.Name != template.Name)
        {
            var existingTemplate = await _context.Templates
                .FirstOrDefaultAsync(t => t.TenantId == tenantId && t.Name == request.Name && t.Id != id, cancellationToken);

            if (existingTemplate != null)
            {
                throw new InvalidOperationException($"A template with the name '{request.Name}' already exists");
            }

            template.Name = request.Name;
        }

        // Update fields if provided
        if (request.Subject != null)
            template.Subject = request.Subject;

        if (request.HtmlBody != null)
            template.HtmlBody = request.HtmlBody;

        if (request.TextBody != null)
            template.TextBody = request.TextBody;

        // Increment version on any content change
        template.Version++;

        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Template '{TemplateName}' (ID: {TemplateId}) updated to version {Version}",
            template.Name, id, template.Version);

        return MapToResponse(template);
    }

    public async Task DeleteTemplateAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContext.GetTenantId();

        var template = await _context.Templates
            .FirstOrDefaultAsync(t => t.Id == id && t.TenantId == tenantId, cancellationToken);

        if (template == null)
        {
            throw new KeyNotFoundException($"Template with ID {id} not found");
        }

        _context.Templates.Remove(template);
        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Template '{TemplateName}' (ID: {TemplateId}) deleted", template.Name, id);
    }

    public async Task<RenderedTemplate> RenderTemplateAsync(Guid templateId, Dictionary<string, string>? variables, CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContext.GetTenantId();

        var template = await _context.Templates
            .FirstOrDefaultAsync(t => t.Id == templateId && t.TenantId == tenantId, cancellationToken);

        if (template == null)
        {
            throw new KeyNotFoundException($"Template with ID {templateId} not found");
        }

        variables ??= [];

        return new RenderedTemplate
        {
            Subject = ReplaceVariables(template.Subject, variables),
            HtmlBody = ReplaceVariables(template.HtmlBody, variables),
            TextBody = ReplaceVariables(template.TextBody, variables)
        };
    }

    private static string? ReplaceVariables(string? content, Dictionary<string, string> variables)
    {
        if (string.IsNullOrEmpty(content))
            return content;

        return VariablePattern().Replace(content, match =>
        {
            var variableName = match.Groups[1].Value;
            return variables.TryGetValue(variableName, out var value) ? value : match.Value;
        });
    }

    private static List<string> ExtractVariables(Templates template)
    {
        var variables = new HashSet<string>();

        ExtractVariablesFromContent(template.Subject, variables);
        ExtractVariablesFromContent(template.HtmlBody, variables);
        ExtractVariablesFromContent(template.TextBody, variables);

        return [.. variables.OrderBy(v => v)];
    }

    private static void ExtractVariablesFromContent(string? content, HashSet<string> variables)
    {
        if (string.IsNullOrEmpty(content))
            return;

        foreach (Match match in VariablePattern().Matches(content))
        {
            variables.Add(match.Groups[1].Value);
        }
    }

    private static TemplateResponse MapToResponse(Templates template)
    {
        return new TemplateResponse
        {
            Id = template.Id,
            Name = template.Name,
            Version = template.Version,
            Subject = template.Subject,
            HtmlBody = template.HtmlBody,
            TextBody = template.TextBody,
            Variables = ExtractVariables(template),
            CreatedAtUtc = template.CreatedAtUtc
        };
    }
}
