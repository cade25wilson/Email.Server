using Amazon.SimpleEmailV2;
using Amazon.SimpleEmailV2.Model;
using Email.Server.Services.Interfaces;

namespace Email.Server.Services.Implementations;

public class SystemEmailService : ISystemEmailService
{
    private readonly IAmazonSimpleEmailServiceV2 _sesClient;
    private readonly IConfiguration _configuration;
    private readonly ILogger<SystemEmailService> _logger;

    public SystemEmailService(
        ISesClientFactory sesClientFactory,
        IConfiguration configuration,
        ILogger<SystemEmailService> logger)
    {
        var region = configuration["AWS:Region"] ?? "us-east-1";
        _sesClient = sesClientFactory.CreateClient(region);
        _configuration = configuration;
        _logger = logger;
    }

    public async Task SendVerificationEmailAsync(string toEmail, string userId, string verificationToken, CancellationToken cancellationToken = default)
    {
        var fromEmail = _configuration["SystemEmail:FromEmail"]
            ?? throw new InvalidOperationException("SystemEmail:FromEmail is not configured");
        var fromName = _configuration["SystemEmail:FromName"] ?? "Email Platform";
        var baseUrl = _configuration["SystemEmail:BaseUrl"]
            ?? throw new InvalidOperationException("SystemEmail:BaseUrl is not configured");

        var apiBaseUrl = _configuration["SystemEmail:ApiBaseUrl"] ?? baseUrl;
        var verificationLink = $"{apiBaseUrl}/api/auth/verify-email?userId={userId}&token={Uri.EscapeDataString(verificationToken)}";

        var htmlBody = $@"
<!DOCTYPE html>
<html>
<head>
    <meta charset=""utf-8"">
    <title>Verify Your Email</title>
</head>
<body style=""font-family: Arial, sans-serif; line-height: 1.6; color: #333; max-width: 600px; margin: 0 auto; padding: 20px;"">
    <div style=""background-color: #f8f9fa; padding: 20px; border-radius: 8px;"">
        <h1 style=""color: #2563eb; margin-bottom: 20px;"">Verify Your Email Address</h1>
        <p>Thank you for registering! Please click the button below to verify your email address:</p>
        <div style=""text-align: center; margin: 30px 0;"">
            <a href=""{verificationLink}""
               style=""background-color: #2563eb; color: white; padding: 12px 24px; text-decoration: none; border-radius: 6px; display: inline-block; font-weight: bold;"">
                Verify Email
            </a>
        </div>
        <p style=""color: #666; font-size: 14px;"">If you didn't create an account, you can safely ignore this email.</p>
        <p style=""color: #666; font-size: 14px;"">This link will expire in 24 hours.</p>
        <hr style=""border: none; border-top: 1px solid #ddd; margin: 20px 0;"">
        <p style=""color: #999; font-size: 12px;"">If the button doesn't work, copy and paste this link into your browser:</p>
        <p style=""color: #999; font-size: 12px; word-break: break-all;"">{verificationLink}</p>
    </div>
</body>
</html>";

        var textBody = $@"
Verify Your Email Address

Thank you for registering! Please click the link below to verify your email address:

{verificationLink}

If you didn't create an account, you can safely ignore this email.

This link will expire in 24 hours.
";

        var configurationSetName = _configuration["SES:DefaultConfigurationSetName"];

        var request = new SendEmailRequest
        {
            FromEmailAddress = $"{fromName} <{fromEmail}>",
            Destination = new Destination
            {
                ToAddresses = [toEmail]
            },
            Content = new EmailContent
            {
                Simple = new Message
                {
                    Subject = new Content { Data = "Verify Your Email Address" },
                    Body = new Body
                    {
                        Html = new Content { Data = htmlBody },
                        Text = new Content { Data = textBody }
                    }
                }
            },
            ConfigurationSetName = configurationSetName
        };

        try
        {
            var response = await _sesClient.SendEmailAsync(request, cancellationToken);
            _logger.LogInformation("Verification email sent to {Email}. MessageId: {MessageId}", toEmail, response.MessageId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send verification email to {Email}", toEmail);
            throw;
        }
    }

    public async Task SendPasswordResetEmailAsync(string toEmail, string resetToken, CancellationToken cancellationToken = default)
    {
        var fromEmail = _configuration["SystemEmail:FromEmail"]
            ?? throw new InvalidOperationException("SystemEmail:FromEmail is not configured");
        var fromName = _configuration["SystemEmail:FromName"] ?? "Email Platform";
        var baseUrl = _configuration["SystemEmail:BaseUrl"]
            ?? throw new InvalidOperationException("SystemEmail:BaseUrl is not configured");

        var resetLink = $"{baseUrl}/reset-password?token={Uri.EscapeDataString(resetToken)}";

        var htmlBody = $@"
<!DOCTYPE html>
<html>
<head>
    <meta charset=""utf-8"">
    <title>Reset Your Password</title>
</head>
<body style=""font-family: Arial, sans-serif; line-height: 1.6; color: #333; max-width: 600px; margin: 0 auto; padding: 20px;"">
    <div style=""background-color: #f8f9fa; padding: 20px; border-radius: 8px;"">
        <h1 style=""color: #2563eb; margin-bottom: 20px;"">Reset Your Password</h1>
        <p>We received a request to reset your password. Click the button below to create a new password:</p>
        <div style=""text-align: center; margin: 30px 0;"">
            <a href=""{resetLink}""
               style=""background-color: #2563eb; color: white; padding: 12px 24px; text-decoration: none; border-radius: 6px; display: inline-block; font-weight: bold;"">
                Reset Password
            </a>
        </div>
        <p style=""color: #666; font-size: 14px;"">If you didn't request a password reset, you can safely ignore this email.</p>
        <p style=""color: #666; font-size: 14px;"">This link will expire in 1 hour.</p>
        <hr style=""border: none; border-top: 1px solid #ddd; margin: 20px 0;"">
        <p style=""color: #999; font-size: 12px;"">If the button doesn't work, copy and paste this link into your browser:</p>
        <p style=""color: #999; font-size: 12px; word-break: break-all;"">{resetLink}</p>
    </div>
</body>
</html>";

        var textBody = $@"
Reset Your Password

We received a request to reset your password. Click the link below to create a new password:

{resetLink}

If you didn't request a password reset, you can safely ignore this email.

This link will expire in 1 hour.
";

        var configurationSetName = _configuration["SES:DefaultConfigurationSetName"];

        var request = new SendEmailRequest
        {
            FromEmailAddress = $"{fromName} <{fromEmail}>",
            Destination = new Destination
            {
                ToAddresses = [toEmail]
            },
            Content = new EmailContent
            {
                Simple = new Message
                {
                    Subject = new Content { Data = "Reset Your Password" },
                    Body = new Body
                    {
                        Html = new Content { Data = htmlBody },
                        Text = new Content { Data = textBody }
                    }
                }
            },
            ConfigurationSetName = configurationSetName
        };

        try
        {
            var response = await _sesClient.SendEmailAsync(request, cancellationToken);
            _logger.LogInformation("Password reset email sent to {Email}. MessageId: {MessageId}", toEmail, response.MessageId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send password reset email to {Email}", toEmail);
            throw;
        }
    }
}
