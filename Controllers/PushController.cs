using Email.Server.DTOs.Requests;
using Email.Shared.DTOs.Requests;
using Email.Shared.DTOs.Responses;
using Email.Shared.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Email.Server.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
[Authorize(AuthenticationSchemes = "ApiKey,Bearer")]
public class PushController(
    IPushService pushService,
    IPushCredentialService credentialService,
    IPushDeviceService deviceService,
    IPushTemplateService templateService,
    ILogger<PushController> logger) : ControllerBase
{
    private readonly IPushService _pushService = pushService;
    private readonly IPushCredentialService _credentialService = credentialService;
    private readonly IPushDeviceService _deviceService = deviceService;
    private readonly IPushTemplateService _templateService = templateService;
    private readonly ILogger<PushController> _logger = logger;

    #region Push Sending

    /// <summary>
    /// Send a push notification
    /// </summary>
    [HttpPost("send")]
    [Authorize(Policy = "push:send")]
    public async Task<IActionResult> SendPush([FromBody] SendPushRequest request, CancellationToken cancellationToken)
    {
        try
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var result = await _pushService.SendPushAsync(request, cancellationToken);

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
            _logger.LogError(ex, "Error sending push notification");
            return StatusCode(500, new { error = "An error occurred while sending the push notification" });
        }
    }

    /// <summary>
    /// List push notifications with pagination and filtering
    /// </summary>
    [HttpGet]
    [Authorize(Policy = "push:read")]
    public async Task<IActionResult> ListPush([FromQuery] PushQueryParams query, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _pushService.ListPushAsync(query, cancellationToken);
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing push notifications");
            return StatusCode(500, new { error = "An error occurred while listing push notifications" });
        }
    }

    /// <summary>
    /// Get a single push notification by ID
    /// </summary>
    [HttpGet("{id:guid}")]
    [Authorize(Policy = "push:read")]
    public async Task<IActionResult> GetPush(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            var message = await _pushService.GetPushAsync(id, cancellationToken);

            if (message == null)
            {
                return NotFound(new { error = $"Push notification {id} not found" });
            }

            return Ok(new PushMessageResponse
            {
                Id = message.Id,
                CredentialId = message.CredentialId,
                DeviceTokenId = message.DeviceTokenId,
                ExternalUserId = message.ExternalUserId,
                Title = message.Title,
                Body = message.Body,
                Status = message.Status,
                TargetCount = message.TargetCount,
                DeliveredCount = message.DeliveredCount,
                RequestedAtUtc = message.RequestedAtUtc,
                ScheduledAtUtc = message.ScheduledAtUtc,
                SentAtUtc = message.SentAtUtc,
                Error = message.Error
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting push notification {PushId}", id);
            return StatusCode(500, new { error = "An error occurred while retrieving the push notification" });
        }
    }

    /// <summary>
    /// Cancel a scheduled push notification (Status=4 only)
    /// </summary>
    [HttpPost("{id:guid}/cancel")]
    [Authorize(Policy = "push:write")]
    public async Task<IActionResult> CancelPush(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _pushService.CancelScheduledPushAsync(id, cancellationToken);

            if (!result)
            {
                return NotFound(new { error = $"Scheduled push notification {id} not found" });
            }

            return Ok(new { message = "Push notification cancelled successfully" });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error cancelling push notification {PushId}", id);
            return StatusCode(500, new { error = "An error occurred while cancelling the push notification" });
        }
    }

    #endregion

    #region Credentials

    /// <summary>
    /// List push credentials
    /// </summary>
    [HttpGet("credentials")]
    [Authorize(Policy = "push:read")]
    public async Task<IActionResult> ListCredentials(CancellationToken cancellationToken)
    {
        try
        {
            var result = await _credentialService.ListCredentialsAsync(cancellationToken);
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing push credentials");
            return StatusCode(500, new { error = "An error occurred while listing credentials" });
        }
    }

    /// <summary>
    /// Create a new push credential
    /// </summary>
    [HttpPost("credentials")]
    [Authorize(Policy = "push:write")]
    public async Task<IActionResult> CreateCredential([FromBody] CreatePushCredentialRequest request, CancellationToken cancellationToken)
    {
        try
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var result = await _credentialService.CreateCredentialAsync(request, cancellationToken);
            return CreatedAtAction(nameof(GetCredential), new { id = result.Id }, result);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating push credential");
            return StatusCode(500, new { error = "An error occurred while creating the credential" });
        }
    }

    /// <summary>
    /// Get a push credential by ID
    /// </summary>
    [HttpGet("credentials/{id:guid}")]
    [Authorize(Policy = "push:read")]
    public async Task<IActionResult> GetCredential(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            var credential = await _credentialService.GetCredentialAsync(id, cancellationToken);

            if (credential == null)
            {
                return NotFound(new { error = $"Credential {id} not found" });
            }

            return Ok(new PushCredentialResponse
            {
                Id = credential.Id,
                Platform = credential.Platform,
                Name = credential.Name,
                ApplicationId = credential.ApplicationId,
                KeyId = credential.KeyId,
                TeamId = credential.TeamId,
                Status = credential.Status,
                StatusMessage = credential.StatusMessage,
                IsDefault = credential.IsDefault,
                IsActive = credential.IsActive,
                ValidatedAtUtc = credential.ValidatedAtUtc,
                ExpiresAtUtc = credential.ExpiresAtUtc,
                CreatedAtUtc = credential.CreatedAtUtc,
                UpdatedAtUtc = credential.UpdatedAtUtc
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting credential {CredentialId}", id);
            return StatusCode(500, new { error = "An error occurred while retrieving the credential" });
        }
    }

    /// <summary>
    /// Update a push credential
    /// </summary>
    [HttpPatch("credentials/{id:guid}")]
    [Authorize(Policy = "push:write")]
    public async Task<IActionResult> UpdateCredential(Guid id, [FromBody] UpdatePushCredentialRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _credentialService.UpdateCredentialAsync(id, request, cancellationToken);

            if (result == null)
            {
                return NotFound(new { error = $"Credential {id} not found" });
            }

            return Ok(result);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating credential {CredentialId}", id);
            return StatusCode(500, new { error = "An error occurred while updating the credential" });
        }
    }

    /// <summary>
    /// Delete a push credential
    /// </summary>
    [HttpDelete("credentials/{id:guid}")]
    [Authorize(Policy = "push:write")]
    public async Task<IActionResult> DeleteCredential(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _credentialService.DeleteCredentialAsync(id, cancellationToken);

            if (!result)
            {
                return NotFound(new { error = $"Credential {id} not found" });
            }

            return NoContent();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting credential {CredentialId}", id);
            return StatusCode(500, new { error = "An error occurred while deleting the credential" });
        }
    }

    /// <summary>
    /// Set a credential as default for its platform
    /// </summary>
    [HttpPost("credentials/{id:guid}/set-default")]
    [Authorize(Policy = "push:write")]
    public async Task<IActionResult> SetDefaultCredential(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _credentialService.SetDefaultCredentialAsync(id, cancellationToken);

            if (!result)
            {
                return NotFound(new { error = $"Credential {id} not found" });
            }

            return Ok(new { message = "Default credential updated successfully" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error setting default credential {CredentialId}", id);
            return StatusCode(500, new { error = "An error occurred while setting the default credential" });
        }
    }

    /// <summary>
    /// Validate a push credential
    /// </summary>
    [HttpPost("credentials/{id:guid}/validate")]
    [Authorize(Policy = "push:write")]
    public async Task<IActionResult> ValidateCredential(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _credentialService.ValidateCredentialAsync(id, cancellationToken);

            return Ok(new { valid = result, message = result ? "Credential is valid" : "Credential validation failed" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating credential {CredentialId}", id);
            return StatusCode(500, new { error = "An error occurred while validating the credential" });
        }
    }

    #endregion

    #region Devices

    /// <summary>
    /// List device tokens
    /// </summary>
    [HttpGet("devices")]
    [Authorize(Policy = "push:read")]
    public async Task<IActionResult> ListDevices([FromQuery] DeviceQueryParams query, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _deviceService.ListDevicesAsync(query, cancellationToken);
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing devices");
            return StatusCode(500, new { error = "An error occurred while listing devices" });
        }
    }

    /// <summary>
    /// Register a device token
    /// </summary>
    [HttpPost("devices")]
    [Authorize(Policy = "push:write")]
    public async Task<IActionResult> RegisterDevice([FromBody] RegisterDeviceRequest request, CancellationToken cancellationToken)
    {
        try
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var result = await _deviceService.RegisterDeviceAsync(request, cancellationToken);
            return CreatedAtAction(nameof(GetDevice), new { id = result.Id }, result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error registering device");
            return StatusCode(500, new { error = "An error occurred while registering the device" });
        }
    }

    /// <summary>
    /// Get a device by ID
    /// </summary>
    [HttpGet("devices/{id:guid}")]
    [Authorize(Policy = "push:read")]
    public async Task<IActionResult> GetDevice(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            var device = await _deviceService.GetDeviceAsync(id, cancellationToken);

            if (device == null)
            {
                return NotFound(new { error = $"Device {id} not found" });
            }

            return Ok(new PushDeviceResponse
            {
                Id = device.Id,
                CredentialId = device.CredentialId,
                Token = device.Token,
                Platform = device.Platform,
                ExternalUserId = device.ExternalUserId,
                IsActive = device.IsActive,
                CreatedAtUtc = device.CreatedAtUtc,
                LastSeenAtUtc = device.LastSeenAtUtc
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting device {DeviceId}", id);
            return StatusCode(500, new { error = "An error occurred while retrieving the device" });
        }
    }

    /// <summary>
    /// Update a device
    /// </summary>
    [HttpPatch("devices/{id:guid}")]
    [Authorize(Policy = "push:write")]
    public async Task<IActionResult> UpdateDevice(Guid id, [FromBody] UpdateDeviceRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _deviceService.UpdateDeviceAsync(id, request, cancellationToken);

            if (result == null)
            {
                return NotFound(new { error = $"Device {id} not found" });
            }

            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating device {DeviceId}", id);
            return StatusCode(500, new { error = "An error occurred while updating the device" });
        }
    }

    /// <summary>
    /// Unregister a device
    /// </summary>
    [HttpDelete("devices/{id:guid}")]
    [Authorize(Policy = "push:write")]
    public async Task<IActionResult> UnregisterDevice(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _deviceService.UnregisterDeviceAsync(id, cancellationToken);

            if (!result)
            {
                return NotFound(new { error = $"Device {id} not found" });
            }

            return NoContent();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error unregistering device {DeviceId}", id);
            return StatusCode(500, new { error = "An error occurred while unregistering the device" });
        }
    }

    #endregion

    #region Templates

    /// <summary>
    /// List push templates
    /// </summary>
    [HttpGet("templates")]
    [Authorize(Policy = "push:read")]
    public async Task<IActionResult> ListTemplates([FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await _templateService.ListTemplatesAsync(page, pageSize, cancellationToken);
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing push templates");
            return StatusCode(500, new { error = "An error occurred while listing templates" });
        }
    }

    /// <summary>
    /// Get a push template by ID
    /// </summary>
    [HttpGet("templates/{id:guid}")]
    [Authorize(Policy = "push:read")]
    public async Task<IActionResult> GetTemplate(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            var template = await _templateService.GetTemplateAsync(id, cancellationToken);

            if (template == null)
            {
                return NotFound(new { error = $"Template {id} not found" });
            }

            return Ok(new PushTemplateResponse
            {
                Id = template.Id,
                Name = template.Name,
                Title = template.Title,
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
    /// Create a new push template
    /// </summary>
    [HttpPost("templates")]
    [Authorize(Policy = "push:write")]
    public async Task<IActionResult> CreateTemplate([FromBody] CreatePushTemplateRequest request, CancellationToken cancellationToken)
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
            _logger.LogError(ex, "Error creating push template");
            return StatusCode(500, new { error = "An error occurred while creating the template" });
        }
    }

    /// <summary>
    /// Update a push template
    /// </summary>
    [HttpPatch("templates/{id:guid}")]
    [Authorize(Policy = "push:write")]
    public async Task<IActionResult> UpdateTemplate(Guid id, [FromBody] UpdatePushTemplateRequest request, CancellationToken cancellationToken)
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
    /// Delete a push template
    /// </summary>
    [HttpDelete("templates/{id:guid}")]
    [Authorize(Policy = "push:write")]
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
}
