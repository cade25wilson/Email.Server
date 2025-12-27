using Email.Server.Attributes;
using Email.Server.DTOs.Requests;
using Email.Server.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Email.Server.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
[Authorize(AuthenticationSchemes = "ApiKey,Bearer")]
[FeatureDisabled("Email templates")]
public class TemplatesController(ITemplateService templateService, ILogger<TemplatesController> logger) : ControllerBase
{
    private readonly ITemplateService _templateService = templateService;
    private readonly ILogger<TemplatesController> _logger = logger;

    /// <summary>
    /// Create a new email template
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> CreateTemplate([FromBody] CreateTemplateRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _templateService.CreateTemplateAsync(request, cancellationToken);
            return CreatedAtAction(nameof(GetTemplate), new { id = result.Id }, result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating template");
            return StatusCode(500, new { error = "An error occurred while creating the template" });
        }
    }

    /// <summary>
    /// Get all templates for the current tenant
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetTemplates(CancellationToken cancellationToken)
    {
        try
        {
            var templates = await _templateService.GetTemplatesAsync(cancellationToken);
            return Ok(templates);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting templates");
            return StatusCode(500, new { error = "An error occurred while retrieving templates" });
        }
    }

    /// <summary>
    /// Get a specific template by ID
    /// </summary>
    [HttpGet("{id}")]
    public async Task<IActionResult> GetTemplate(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            var template = await _templateService.GetTemplateAsync(id, cancellationToken);
            return Ok(template);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting template {TemplateId}", id);
            return StatusCode(500, new { error = "An error occurred while retrieving the template" });
        }
    }

    /// <summary>
    /// Update an existing template
    /// </summary>
    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateTemplate(Guid id, [FromBody] UpdateTemplateRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _templateService.UpdateTemplateAsync(id, request, cancellationToken);
            return Ok(result);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating template {TemplateId}", id);
            return StatusCode(500, new { error = "An error occurred while updating the template" });
        }
    }

    /// <summary>
    /// Delete a template
    /// </summary>
    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteTemplate(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            await _templateService.DeleteTemplateAsync(id, cancellationToken);
            return NoContent();
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting template {TemplateId}", id);
            return StatusCode(500, new { error = "An error occurred while deleting the template" });
        }
    }

    /// <summary>
    /// Preview a template with sample variables
    /// </summary>
    [HttpPost("{id}/preview")]
    public async Task<IActionResult> PreviewTemplate(Guid id, [FromBody] Dictionary<string, string>? variables, CancellationToken cancellationToken)
    {
        try
        {
            var rendered = await _templateService.RenderTemplateAsync(id, variables, cancellationToken);
            return Ok(rendered);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error previewing template {TemplateId}", id);
            return StatusCode(500, new { error = "An error occurred while previewing the template" });
        }
    }
}
