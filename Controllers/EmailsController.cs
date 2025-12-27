using Email.Server.Attributes;
using Email.Server.DTOs.Requests;
using Email.Server.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Email.Server.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
[Authorize(AuthenticationSchemes = "ApiKey,Bearer")]
[FeatureDisabled("Email API")]
public class EmailsController(
    IEmailSendingService emailService,
    IMessageService messageService,
    ILogger<EmailsController> logger) : ControllerBase
{
    private readonly IEmailSendingService _emailService = emailService;
    private readonly IMessageService _messageService = messageService;
    private readonly ILogger<EmailsController> _logger = logger;

    /// <summary>
    /// Send a single email
    /// </summary>
    [HttpPost("send")]
    [Authorize(Policy = "emails:send")]
    public async Task<IActionResult> SendEmail([FromBody] SendEmailRequest request, CancellationToken cancellationToken)
    {
        try
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var result = await _emailService.SendEmailAsync(request, cancellationToken);

            if (result.Status == 2) // Failed
            {
                return BadRequest(new { error = result.Error, messageId = result.MessageId });
            }

            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending email");
            return StatusCode(500, new { error = "An error occurred while sending the email" });
        }
    }

    /// <summary>
    /// Send multiple emails in a batch (max 100)
    /// </summary>
    [HttpPost("batch")]
    [Authorize(Policy = "emails:send")]
    public async Task<IActionResult> SendBatch([FromBody] SendBatchEmailRequest request, CancellationToken cancellationToken)
    {
        try
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var result = await _emailService.SendBatchEmailsAsync(request, cancellationToken);
            return Ok(result);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending batch emails");
            return StatusCode(500, new { error = "An error occurred while sending the batch" });
        }
    }

    /// <summary>
    /// List emails with pagination, filtering, and sorting
    /// </summary>
    [HttpGet]
    [Authorize(Policy = "emails:read")]
    public async Task<IActionResult> ListEmails([FromQuery] ListEmailsRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _messageService.ListEmailsAsync(request, cancellationToken);
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing emails");
            return StatusCode(500, new { error = "An error occurred while listing emails" });
        }
    }

    /// <summary>
    /// Get a single email by ID
    /// </summary>
    [HttpGet("{id:guid}")]
    [Authorize(Policy = "emails:read")]
    public async Task<IActionResult> GetEmail(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _messageService.GetMessageAsync(id, cancellationToken);
            return Ok(result);
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new { error = $"Email {id} not found" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting email {EmailId}", id);
            return StatusCode(500, new { error = "An error occurred while retrieving the email" });
        }
    }

    /// <summary>
    /// Update a scheduled email (Status=4 only)
    /// </summary>
    [HttpPatch("{id:guid}")]
    [Authorize(Policy = "emails:write")]
    public async Task<IActionResult> UpdateEmail(Guid id, [FromBody] UpdateScheduledEmailRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _messageService.UpdateScheduledEmailAsync(id, request, cancellationToken);
            return Ok(result);
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new { error = $"Email {id} not found" });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating email {EmailId}", id);
            return StatusCode(500, new { error = "An error occurred while updating the email" });
        }
    }

    /// <summary>
    /// Cancel a scheduled email (Status=4 only)
    /// </summary>
    [HttpPost("{id:guid}/cancel")]
    [Authorize(Policy = "emails:write")]
    public async Task<IActionResult> CancelEmail(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            await _messageService.CancelScheduledEmailAsync(id, cancellationToken);
            return Ok(new { message = "Email cancelled successfully" });
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new { error = $"Email {id} not found" });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error cancelling email {EmailId}", id);
            return StatusCode(500, new { error = "An error occurred while cancelling the email" });
        }
    }

    /// <summary>
    /// List attachments for an email
    /// </summary>
    [HttpGet("{emailId:guid}/attachments")]
    [Authorize(Policy = "emails:read")]
    public async Task<IActionResult> ListAttachments(Guid emailId, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _messageService.GetAttachmentsAsync(emailId, cancellationToken);
            return Ok(result);
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new { error = $"Email {emailId} not found" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing attachments for email {EmailId}", emailId);
            return StatusCode(500, new { error = "An error occurred while listing attachments" });
        }
    }

    /// <summary>
    /// Get a signed download URL for an attachment
    /// </summary>
    [HttpGet("{emailId:guid}/attachments/{attachmentId:long}")]
    [Authorize(Policy = "emails:read")]
    public async Task<IActionResult> GetAttachment(Guid emailId, long attachmentId, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _messageService.GetAttachmentDownloadUrlAsync(emailId, attachmentId, cancellationToken);
            return Ok(result);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting attachment {AttachmentId} for email {EmailId}", attachmentId, emailId);
            return StatusCode(500, new { error = "An error occurred while retrieving the attachment" });
        }
    }
}
