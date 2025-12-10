using Email.Server.DTOs.Requests;
using Email.Server.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Email.Server.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
public class DomainsController(IDomainManagementService domainService, ILogger<DomainsController> logger) : ControllerBase
{
    private readonly IDomainManagementService _domainService = domainService;
    private readonly ILogger<DomainsController> _logger = logger;

    [HttpPost]
    [Authorize(Policy = "domains:write")]
    public async Task<IActionResult> CreateDomain([FromBody] CreateDomainRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _domainService.CreateDomainAsync(request, cancellationToken);
            return CreatedAtAction(nameof(GetDomain), new { id = result.Id }, result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating domain");
            return StatusCode(500, new { error = "An error occurred while creating the domain" });
        }
    }

    [HttpGet]
    [Authorize(Policy = "domains:read")]
    public async Task<IActionResult> GetDomains(CancellationToken cancellationToken)
    {
        try
        {
            var domains = await _domainService.GetDomainsAsync(cancellationToken);
            return Ok(domains);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting domains");
            return StatusCode(500, new { error = "An error occurred while retrieving domains" });
        }
    }

    [HttpGet("{id}")]
    [Authorize(Policy = "domains:read")]
    public async Task<IActionResult> GetDomain(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            var domain = await _domainService.GetDomainAsync(id, cancellationToken);
            return Ok(domain);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting domain {DomainId}", id);
            return StatusCode(500, new { error = "An error occurred while retrieving the domain" });
        }
    }

    [HttpGet("{id}/verify")]
    [Authorize(Policy = "domains:write")]
    public async Task<IActionResult> VerifyDomain(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            var domain = await _domainService.VerifyDomainAsync(id, cancellationToken);
            return Ok(domain);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error verifying domain {DomainId}", id);
            return StatusCode(500, new { error = "An error occurred while verifying the domain" });
        }
    }

    [HttpDelete("{id}")]
    [Authorize(Policy = "domains:delete")]
    public async Task<IActionResult> DeleteDomain(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            await _domainService.DeleteDomainAsync(id, cancellationToken);
            return NoContent();
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting domain {DomainId}", id);
            return StatusCode(500, new { error = "An error occurred while deleting the domain" });
        }
    }

    [HttpPost("{id}/inbound/enable")]
    [Authorize(Policy = "domains:write")]
    public async Task<IActionResult> EnableInbound(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            var domain = await _domainService.EnableInboundAsync(id, cancellationToken);
            return Ok(domain);
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
            _logger.LogError(ex, "Error enabling inbound for domain {DomainId}", id);
            return StatusCode(500, new { error = "An error occurred while enabling inbound email" });
        }
    }

    [HttpPost("{id}/inbound/disable")]
    [Authorize(Policy = "domains:write")]
    public async Task<IActionResult> DisableInbound(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            var domain = await _domainService.DisableInboundAsync(id, cancellationToken);
            return Ok(domain);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error disabling inbound for domain {DomainId}", id);
            return StatusCode(500, new { error = "An error occurred while disabling inbound email" });
        }
    }
}
