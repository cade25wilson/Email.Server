using System.Threading.RateLimiting;
using Amazon.S3;
using Email.Server.Authentication;
using Email.Server.Configuration;
using Email.Server.Data;
using Email.Server.DTOs.Requests;
using Email.Server.Mapping;
using Email.Server.Services.Implementations;
using Email.Server.Services.Interfaces;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Serilog;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.Identity.Web;
using Scalar.AspNetCore;

// Configure Serilog
var environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production";
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(new ConfigurationBuilder()
        .AddJsonFile("appsettings.json")
        .AddJsonFile($"appsettings.{environment}.json", optional: true)
        .Build())
    .CreateLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    // Add Serilog
    builder.Host.UseSerilog();

    // Add services to the container.

    builder.Services.AddDbContext<ApplicationDbContext>(options =>
    {
        var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("DefaultConnection is not configured");
        options.UseSqlServer(connectionString);
    });

    // Note: ASP.NET Identity has been removed - we now use Entra ID for authentication
    // Users are managed in Entra, not in the local database

    // Configure Entra ID Authentication
    var azureAdConfig = builder.Configuration.GetSection("AzureAd");
    builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer(options =>
        {
            var instance = azureAdConfig["Instance"]?.TrimEnd('/') ?? "";
            var tenantId = azureAdConfig["TenantId"];
            var clientId = azureAdConfig["ClientId"];

            // Ensure instance has https:// scheme
            if (!instance.StartsWith("https://", StringComparison.OrdinalIgnoreCase) &&
                !instance.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            {
                instance = $"https://{instance}";
            }

            options.Authority = $"{instance}/{tenantId}/v2.0";
            options.Audience = clientId;

            options.TokenValidationParameters = new Microsoft.IdentityModel.Tokens.TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuers =
                [
                    $"https://{tenantId}.ciamlogin.com/{tenantId}/v2.0",
                    $"{instance}/{tenantId}/v2.0",
                    $"https://login.microsoftonline.com/{tenantId}/v2.0",
                    $"https://sts.windows.net/{tenantId}/",
                ],
                ValidateAudience = true,
                ValidAudience = clientId,
                ValidateLifetime = true,
                NameClaimType = "name",
            };

            // Add detailed logging for debugging auth issues
            options.Events = new JwtBearerEvents
            {
                OnAuthenticationFailed = context =>
                {
                    Log.Error("Authentication failed: {Error}", context.Exception.Message);
                    if (context.Exception.InnerException != null)
                    {
                        Log.Error("Inner exception: {InnerError}", context.Exception.InnerException.Message);
                    }
                    return Task.CompletedTask;
                },
                OnTokenValidated = context =>
                {
                    Log.Information("Token validated for user: {User}",
                        context.Principal?.Identity?.Name ?? "unknown");
                    return Task.CompletedTask;
                },
                OnMessageReceived = context =>
                {
                    var hasAuth = !string.IsNullOrEmpty(context.Request.Headers.Authorization.ToString());
                    Log.Debug("JWT message received, has Authorization header: {HasAuth}", hasAuth);
                    return Task.CompletedTask;
                }
            };
        })
        .AddScheme<ApiKeyAuthenticationOptions, ApiKeyAuthenticationHandler>(
            ApiKeyAuthenticationHandler.SchemeName, null);

    // Configure Authorization with scope-based policies
    builder.Services.AddAuthorizationBuilder()
        .AddPolicy(ApiKeyScopes.EmailsSend, policy =>
            policy.AddRequirements(new ApiKeyScopeRequirement(ApiKeyScopes.EmailsSend)))
        .AddPolicy(ApiKeyScopes.EmailsRead, policy =>
            policy.AddRequirements(new ApiKeyScopeRequirement(ApiKeyScopes.EmailsRead)))
        .AddPolicy(ApiKeyScopes.EmailsWrite, policy =>
            policy.AddRequirements(new ApiKeyScopeRequirement(ApiKeyScopes.EmailsWrite)))
        .AddPolicy(ApiKeyScopes.DomainsRead, policy =>
            policy.AddRequirements(new ApiKeyScopeRequirement(ApiKeyScopes.DomainsRead)))
        .AddPolicy(ApiKeyScopes.DomainsWrite, policy =>
            policy.AddRequirements(new ApiKeyScopeRequirement(ApiKeyScopes.DomainsWrite)))
        .AddPolicy(ApiKeyScopes.DomainsDelete, policy =>
            policy.AddRequirements(new ApiKeyScopeRequirement(ApiKeyScopes.DomainsDelete)))
        .AddPolicy(ApiKeyScopes.MessagesRead, policy =>
            policy.AddRequirements(new ApiKeyScopeRequirement(ApiKeyScopes.MessagesRead)));

    // Register authorization handler
    builder.Services.AddSingleton<IAuthorizationHandler, ApiKeyScopeHandler>();

    // Register claims transformation for adding TenantId to Entra tokens
    builder.Services.AddScoped<IClaimsTransformation, TenantClaimsTransformation>();

    // Configure CORS
    builder.Services.AddCors(options =>
    {
        options.AddPolicy("AllowFrontend", policy =>
        {
            policy.WithOrigins(
                    "https://localhost:59592",
                    "http://localhost:5173",
                    "https://localhost:59594",
                    "https://localhost:59593",
                    "https://www.socialhq.app",
                    "https://polite-mud-06a30121e.3.azurestaticapps.net") // Azure Static Web App
                  .AllowAnyHeader()
                  .AllowAnyMethod()
                  .AllowCredentials();
        });
    });

    // Configure AutoMapper
    builder.Services.AddAutoMapper(cfg =>
    {
        cfg.LicenseKey = builder.Configuration["AutoMapper:LicenseKey"];
        cfg.AddProfile<AutoMapperProfile>();
    });

    // Add HttpContextAccessor for tenant context
    builder.Services.AddHttpContextAccessor();

    // Configure Billing Settings
    builder.Services.Configure<StripeSettings>(builder.Configuration.GetSection(StripeSettings.SectionName));
    builder.Services.Configure<BillingSettings>(builder.Configuration.GetSection(BillingSettings.SectionName));

    // Register Services
    builder.Services.AddSingleton<ISesClientFactory, SesClientFactory>();
    builder.Services.AddScoped<ITenantContextService, TenantContextService>();
    builder.Services.AddScoped<ITenantManagementService, TenantManagementService>();
    builder.Services.AddScoped<ISesClientService, SesClientService>();
    builder.Services.AddScoped<IDomainManagementService, DomainManagementService>();
    builder.Services.AddScoped<IEmailSendingService, EmailSendingService>();
    builder.Services.AddScoped<ISystemEmailService, SystemEmailService>();
    builder.Services.AddScoped<IMessageService, MessageService>();
    builder.Services.AddScoped<IApiKeyService, ApiKeyService>();
    builder.Services.AddScoped<ITemplateService, TemplateService>();
    builder.Services.AddScoped<IWebhookDeliveryService, WebhookDeliveryService>();
    builder.Services.AddScoped<ISesNotificationService, SesNotificationService>();

    // AWS S3 for email attachments and inbound emails
    builder.Services.AddSingleton<IAmazonS3>(sp =>
    {
        var config = new AmazonS3Config
        {
            RegionEndpoint = Amazon.RegionEndpoint.GetBySystemName(
                builder.Configuration["AWS:Region"] ?? "us-west-2")
        };
        return new AmazonS3Client(config);
    });
    builder.Services.AddScoped<IAttachmentStorageService, AttachmentStorageService>();
    builder.Services.AddScoped<IInboundEmailStorageService, InboundEmailStorageService>();
    builder.Services.AddScoped<IInboundEmailService, InboundEmailService>();

    // Billing Services
    builder.Services.AddScoped<IBillingService, BillingService>();
    builder.Services.AddScoped<IUsageTrackingService, UsageTrackingService>();
    builder.Services.AddScoped<IStripeWebhookService, StripeWebhookService>();
    builder.Services.AddScoped<ISubscriptionEnforcementService, SubscriptionEnforcementService>();

    // Add HttpClientFactory for webhook confirmations and deliveries
    builder.Services.AddHttpClient();
    builder.Services.AddHttpClient("WebhookClient", client =>
    {
        client.DefaultRequestHeaders.Add("Accept", "application/json");
    });

    // Add rate limiting for public endpoints (contact form)
    builder.Services.AddRateLimiter(options =>
    {
        options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        options.AddFixedWindowLimiter("ContactForm", limiterOptions =>
        {
            limiterOptions.PermitLimit = 5;
            limiterOptions.Window = TimeSpan.FromMinutes(15);
            limiterOptions.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
            limiterOptions.QueueLimit = 0;
        });
    });

    builder.Services.AddControllers()
        .AddJsonOptions(options =>
        {
            // Serialize enums as strings (e.g., "Owner" instead of 0)
            options.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
        });

    // Add OpenAPI (.NET 10 built-in)
    builder.Services.AddOpenApi(options =>
    {
        options.AddDocumentTransformer((document, context, cancellationToken) =>
        {
            document.Info.Title = "EmailAPI";
            document.Info.Version = "v1";
            document.Info.Description = "Transactional email API for sending emails, managing templates, webhooks, and API keys.";
            return Task.CompletedTask;
        });
    });

    var app = builder.Build();

    app.UseDefaultFiles();
    app.UseStaticFiles();

    app.UseHttpsRedirection();

    app.UseCors("AllowFrontend");

    app.UseRateLimiter();

    app.UseAuthentication();
    app.UseAuthorization();

    // OpenAPI & Scalar UI (available in all environments for API documentation)
    app.MapOpenApi();
    app.MapScalarApiReference(options =>
    {
        options
            .WithTitle("EmailAPI")
            .WithTheme(ScalarTheme.Mars)
            .WithDefaultHttpClient(ScalarTarget.CSharp, ScalarClient.HttpClient);
    });

    app.MapControllers();

    app.MapFallbackToFile("/index.html");

    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}
