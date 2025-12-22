using Email.Server.DTOs.Requests;
using Email.Server.DTOs.Responses;
using Email.Server.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Email.Server.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
[Authorize(AuthenticationSchemes = "ApiKey,Bearer")]
public class SmsController(
    ISmsService smsService,
    ISmsTemplateService templateService,
    ISmsPhoneNumberService phoneNumberService,
    ILogger<SmsController> logger) : ControllerBase
{
    private readonly ISmsService _smsService = smsService;
    private readonly ISmsTemplateService _templateService = templateService;
    private readonly ISmsPhoneNumberService _phoneNumberService = phoneNumberService;
    private readonly ILogger<SmsController> _logger = logger;

    #region SMS Sending

    /// <summary>
    /// Send a single SMS message
    /// </summary>
    [HttpPost("send")]
    [Authorize(Policy = "sms:send")]
    public async Task<IActionResult> SendSms([FromBody] SendSmsRequest request, CancellationToken cancellationToken)
    {
        try
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var result = await _smsService.SendSmsAsync(request, cancellationToken);

            if (result.Status == 2) // Failed
            {
                return BadRequest(new { error = result.Error, message_id = result.MessageId });
            }

            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending SMS");
            return StatusCode(500, new { error = "An error occurred while sending the SMS" });
        }
    }

    /// <summary>
    /// List SMS messages with pagination and filtering
    /// </summary>
    [HttpGet]
    [Authorize(Policy = "sms:read")]
    public async Task<IActionResult> ListSms([FromQuery] SmsQueryParams query, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _smsService.ListSmsAsync(query, cancellationToken);
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing SMS messages");
            return StatusCode(500, new { error = "An error occurred while listing SMS messages" });
        }
    }

    /// <summary>
    /// Get a single SMS message by ID
    /// </summary>
    [HttpGet("{id:guid}")]
    [Authorize(Policy = "sms:read")]
    public async Task<IActionResult> GetSms(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            var message = await _smsService.GetSmsAsync(id, cancellationToken);

            if (message == null)
            {
                return NotFound(new { error = $"SMS {id} not found" });
            }

            return Ok(new SmsMessageResponse
            {
                Id = message.Id,
                FromNumber = message.FromNumber,
                ToNumber = message.ToNumber,
                Body = message.Body,
                TemplateId = message.TemplateId,
                AwsMessageId = message.AwsMessageId,
                Status = message.Status,
                SegmentCount = message.SegmentCount,
                RequestedAtUtc = message.RequestedAtUtc,
                ScheduledAtUtc = message.ScheduledAtUtc,
                SentAtUtc = message.SentAtUtc,
                Error = message.Error
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting SMS {SmsId}", id);
            return StatusCode(500, new { error = "An error occurred while retrieving the SMS" });
        }
    }

    /// <summary>
    /// Cancel a scheduled SMS (Status=4 only)
    /// </summary>
    [HttpPost("{id:guid}/cancel")]
    [Authorize(Policy = "sms:write")]
    public async Task<IActionResult> CancelSms(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _smsService.CancelScheduledSmsAsync(id, cancellationToken);

            if (!result)
            {
                return NotFound(new { error = $"Scheduled SMS {id} not found" });
            }

            return Ok(new { message = "SMS cancelled successfully" });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error cancelling SMS {SmsId}", id);
            return StatusCode(500, new { error = "An error occurred while cancelling the SMS" });
        }
    }

    #endregion

    #region SMS Templates

    /// <summary>
    /// List SMS templates
    /// </summary>
    [HttpGet("templates")]
    [Authorize(Policy = "sms:read")]
    public async Task<IActionResult> ListTemplates([FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await _templateService.ListTemplatesAsync(page, pageSize, cancellationToken);
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing SMS templates");
            return StatusCode(500, new { error = "An error occurred while listing templates" });
        }
    }

    /// <summary>
    /// Get a single SMS template by ID
    /// </summary>
    [HttpGet("templates/{id:guid}")]
    [Authorize(Policy = "sms:read")]
    public async Task<IActionResult> GetTemplate(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            var template = await _templateService.GetTemplateAsync(id, cancellationToken);

            if (template == null)
            {
                return NotFound(new { error = $"Template {id} not found" });
            }

            return Ok(new SmsTemplateResponse
            {
                Id = template.Id,
                Name = template.Name,
                Body = template.Body,
                IsActive = template.IsActive,
                CreatedAtUtc = template.CreatedAtUtc,
                UpdatedAtUtc = template.UpdatedAtUtc
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting template {TemplateId}", id);
            return StatusCode(500, new { error = "An error occurred while retrieving the template" });
        }
    }

    /// <summary>
    /// Create a new SMS template
    /// </summary>
    [HttpPost("templates")]
    [Authorize(Policy = "sms:write")]
    public async Task<IActionResult> CreateTemplate([FromBody] CreateSmsTemplateRequest request, CancellationToken cancellationToken)
    {
        try
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var result = await _templateService.CreateTemplateAsync(request, cancellationToken);
            return CreatedAtAction(nameof(GetTemplate), new { id = result.Id }, result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating SMS template");
            return StatusCode(500, new { error = "An error occurred while creating the template" });
        }
    }

    /// <summary>
    /// Update an SMS template
    /// </summary>
    [HttpPatch("templates/{id:guid}")]
    [Authorize(Policy = "sms:write")]
    public async Task<IActionResult> UpdateTemplate(Guid id, [FromBody] UpdateSmsTemplateRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _templateService.UpdateTemplateAsync(id, request, cancellationToken);

            if (result == null)
            {
                return NotFound(new { error = $"Template {id} not found" });
            }

            return Ok(result);
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
    /// Delete an SMS template
    /// </summary>
    [HttpDelete("templates/{id:guid}")]
    [Authorize(Policy = "sms:write")]
    public async Task<IActionResult> DeleteTemplate(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _templateService.DeleteTemplateAsync(id, cancellationToken);

            if (!result)
            {
                return NotFound(new { error = $"Template {id} not found" });
            }

            return NoContent();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting template {TemplateId}", id);
            return StatusCode(500, new { error = "An error occurred while deleting the template" });
        }
    }

    #endregion

    #region Phone Numbers

    /// <summary>
    /// List phone numbers for the tenant
    /// </summary>
    [HttpGet("phone-numbers")]
    [Authorize(Policy = "sms:read")]
    public async Task<IActionResult> ListPhoneNumbers(CancellationToken cancellationToken)
    {
        try
        {
            var result = await _phoneNumberService.ListPhoneNumbersAsync(cancellationToken);
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing phone numbers");
            return StatusCode(500, new { error = "An error occurred while listing phone numbers" });
        }
    }

    /// <summary>
    /// Get a phone number by ID
    /// </summary>
    [HttpGet("phone-numbers/{id:guid}")]
    [Authorize(Policy = "sms:read")]
    public async Task<IActionResult> GetPhoneNumber(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            var phone = await _phoneNumberService.GetPhoneNumberAsync(id, cancellationToken);

            if (phone == null)
            {
                return NotFound(new { error = $"Phone number {id} not found" });
            }

            return Ok(new SmsPhoneNumberResponse
            {
                Id = phone.Id,
                PhoneNumber = phone.PhoneNumber,
                NumberType = phone.NumberType.ToString(),
                Country = phone.Country,
                MonthlyFeeCents = phone.MonthlyFeeCents,
                IsDefault = phone.IsDefault,
                IsActive = phone.IsActive,
                ProvisionedAtUtc = phone.ProvisionedAtUtc,
                CreatedAtUtc = phone.CreatedAtUtc
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting phone number {PhoneNumberId}", id);
            return StatusCode(500, new { error = "An error occurred while retrieving the phone number" });
        }
    }

    /// <summary>
    /// Set a phone number as the default for the tenant
    /// </summary>
    [HttpPost("phone-numbers/{id:guid}/set-default")]
    [Authorize(Policy = "sms:write")]
    public async Task<IActionResult> SetDefaultPhoneNumber(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _phoneNumberService.SetDefaultPhoneNumberAsync(id, cancellationToken);

            if (!result)
            {
                return NotFound(new { error = $"Phone number {id} not found" });
            }

            return Ok(new { message = "Default phone number updated successfully" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error setting default phone number {PhoneNumberId}", id);
            return StatusCode(500, new { error = "An error occurred while setting the default phone number" });
        }
    }

    #endregion
}
