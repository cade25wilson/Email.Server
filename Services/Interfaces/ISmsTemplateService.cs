using Email.Server.DTOs.Requests;
using Email.Server.DTOs.Responses;
using Email.Server.Models;

namespace Email.Server.Services.Interfaces;

public interface ISmsTemplateService
{
    /// <summary>
    /// Creates a new SMS template.
    /// </summary>
    Task<SmsTemplateResponse> CreateTemplateAsync(CreateSmsTemplateRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates an existing SMS template.
    /// </summary>
    Task<SmsTemplateResponse?> UpdateTemplateAsync(Guid id, UpdateSmsTemplateRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a template by ID.
    /// </summary>
    Task<SmsTemplates?> GetTemplateAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a template by name.
    /// </summary>
    Task<SmsTemplates?> GetTemplateByNameAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists all templates for the current tenant.
    /// </summary>
    Task<SmsTemplateListResponse> ListTemplatesAsync(int page = 1, int pageSize = 20, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a template.
    /// </summary>
    Task<bool> DeleteTemplateAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Renders a template with the provided variables.
    /// </summary>
    string RenderTemplate(string templateBody, Dictionary<string, string>? variables);
}
