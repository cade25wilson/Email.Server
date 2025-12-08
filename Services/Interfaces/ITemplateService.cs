using Email.Server.DTOs.Requests;
using Email.Server.DTOs.Responses;

namespace Email.Server.Services.Interfaces;

public interface ITemplateService
{
    Task<TemplateResponse> CreateTemplateAsync(CreateTemplateRequest request, CancellationToken cancellationToken = default);
    Task<TemplateResponse> GetTemplateAsync(Guid id, CancellationToken cancellationToken = default);
    Task<List<TemplateListResponse>> GetTemplatesAsync(CancellationToken cancellationToken = default);
    Task<TemplateResponse> UpdateTemplateAsync(Guid id, UpdateTemplateRequest request, CancellationToken cancellationToken = default);
    Task DeleteTemplateAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Renders a template by replacing variables with provided values.
    /// Variables use the format {{variable_name}} in template content.
    /// </summary>
    Task<RenderedTemplate> RenderTemplateAsync(Guid templateId, Dictionary<string, string>? variables, CancellationToken cancellationToken = default);
}

public class RenderedTemplate
{
    public string? Subject { get; set; }
    public string? HtmlBody { get; set; }
    public string? TextBody { get; set; }
}
