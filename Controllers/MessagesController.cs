using Email.Server.Attributes;
using Email.Server.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Email.Server.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
[Authorize(AuthenticationSchemes = "ApiKey,Bearer")]
[FeatureDisabled("Email messages")]
public class MessagesController(IMessageService messageService, ILogger<MessagesController> logger) : ControllerBase
{
    private readonly IMessageService _messageService = messageService;
    private readonly ILogger<MessagesController> _logger = logger;

    [HttpGet]
    [Authorize(Policy = "messages:read")]
    public async Task<IActionResult> GetMessages([FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken cancellationToken = default)
    {
        try
        {
            var messages = await _messageService.GetMessagesAsync(page, pageSize, cancellationToken);
            return Ok(messages);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting messages");
            return StatusCode(500, new { error = "An error occurred while retrieving messages" });
        }
    }

    [HttpGet("{id}")]
    [Authorize(Policy = "messages:read")]
    public async Task<IActionResult> GetMessage(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            var message = await _messageService.GetMessageAsync(id, cancellationToken);
            return Ok(message);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting message {MessageId}", id);
            return StatusCode(500, new { error = "An error occurred while retrieving the message" });
        }
    }
}
