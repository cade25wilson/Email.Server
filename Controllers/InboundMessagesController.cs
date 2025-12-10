using Email.Server.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Email.Server.Controllers;

[ApiController]
[Route("api/v1/inbound")]
public class InboundMessagesController(
    IInboundEmailService inboundEmailService,
    ILogger<InboundMessagesController> logger) : ControllerBase
{
    private readonly IInboundEmailService _inboundEmailService = inboundEmailService;
    private readonly ILogger<InboundMessagesController> _logger = logger;

    /// <summary>
    /// Get list of inbound messages with pagination
    /// </summary>
    [HttpGet]
    [Authorize(Policy = "messages:read")]
    public async Task<IActionResult> GetInboundMessages(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        [FromQuery] Guid? domainId = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (page < 1) page = 1;
            if (pageSize < 1) pageSize = 50;
            if (pageSize > 100) pageSize = 100;

            var result = await _inboundEmailService.GetInboundMessagesAsync(page, pageSize, domainId, cancellationToken);
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting inbound messages");
            return StatusCode(500, new { error = "An error occurred while retrieving inbound messages" });
        }
    }

    /// <summary>
    /// Get a single inbound message by ID
    /// </summary>
    [HttpGet("{id}")]
    [Authorize(Policy = "messages:read")]
    public async Task<IActionResult> GetInboundMessage(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            var message = await _inboundEmailService.GetInboundMessageAsync(id, cancellationToken);
            if (message == null)
            {
                return NotFound(new { error = $"Inbound message {id} not found" });
            }
            return Ok(message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting inbound message {MessageId}", id);
            return StatusCode(500, new { error = "An error occurred while retrieving the inbound message" });
        }
    }

    /// <summary>
    /// Get a signed download URL for the raw email content
    /// </summary>
    [HttpGet("{id}/raw")]
    [Authorize(Policy = "messages:read")]
    public async Task<IActionResult> GetRawEmail(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _inboundEmailService.GetRawEmailUrlAsync(id, cancellationToken);
            return Ok(result);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting raw email URL for message {MessageId}", id);
            return StatusCode(500, new { error = "An error occurred while generating the download URL" });
        }
    }

    /// <summary>
    /// Delete an inbound message
    /// </summary>
    [HttpDelete("{id}")]
    [Authorize(Policy = "messages:delete")]
    public async Task<IActionResult> DeleteInboundMessage(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            await _inboundEmailService.DeleteInboundMessageAsync(id, cancellationToken);
            return NoContent();
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting inbound message {MessageId}", id);
            return StatusCode(500, new { error = "An error occurred while deleting the inbound message" });
        }
    }
}
