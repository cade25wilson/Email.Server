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
using Microsoft.EntityFrameworkCore;
using Serilog;

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

    // Note: ASP.NET Identity has been removed - we now use Entra External ID for authentication
    // Users are managed in Entra, not in the local database

    // Configure Entra External ID (CIAM) Authentication
    var azureAdConfig = builder.Configuration.GetSection("AzureAd");
    builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer(options =>
        {
            var instance = azureAdConfig["Instance"]?.TrimEnd('/');
            var tenantId = azureAdConfig["TenantId"];
            var clientId = azureAdConfig["ClientId"];

            // For CIAM (External ID), use the ciamlogin.com authority
            // The metadata endpoint will provide the correct issuer
            options.Authority = $"{instance}/{tenantId}/v2.0";
            options.Audience = clientId;

            // Let the middleware auto-discover the issuer from OIDC metadata
            // This ensures we use the exact issuer that's in the token
            options.TokenValidationParameters = new Microsoft.IdentityModel.Tokens.TokenValidationParameters
            {
                ValidateIssuer = true,
                // Don't set ValidIssuer - let it be discovered from the authority metadata
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
                    "https://localhost:" +
                    "59592",
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
        //cfg.LicenseKey = "eyJhbGciOiJSUzI1NiIsImtpZCI6Ikx1Y2t5UGVubnlTb2Z0d2FyZUxpY2Vuc2VLZXkvYmJiMTNhY2I1OTkwNGQ4OWI0Y2IxYzg1ZjA4OGNjZjkiLCJ0eXAiOiJKV1QifQ.eyJpc3MiOiJodHRwczovL2x1Y2t5cGVubnlzb2Z0d2FyZS5jb20iLCJhdWQiOiJMdWNreVBlbm55U29mdHdhcmUiLCJleHAiOiIxNzk1NjUxMjAwIiwiaWF0IjoiMTc2NDE4NTc0OSIsImFjY291bnRfaWQiOiIwMTlhYzFhOTg1MjM3YmFmYWE1YjM5ZGQ2MTFlZjVmNCIsImN1c3RvbWVyX2lkIjoiY3RtXzAxa2IwdG1lNm1rNDYxdDRuZXNlYzhuODZoIiwic3ViX2lkIjoiLSIsImVkaXRpb24iOiIwIiwidHlwZSI6IjIifQ.n0I9BbgcgWWokf5JSvqOocYSx92ls7BhS5kf5HOfCFbu2YMVjoscakiI1NNARIJPgozFgdfDNSs5uXj9DNSE4tTrBmkatRQ7rnj4OtAZehuwAdUefvK8S-MVh61XIxDr6cMkSNqTVj3iilMyeeoVhkHYYMSVKkc5ArI88YhIWWY-ZWmDReYECwG9k9GPTu418zxLc5NHqoXfRGpxIm67MWNmEy425Ru0Q4VcFYZQPUjN7setwVVRLPBh3uxsDVzPY5ErWa9OayOPsQ7AFzGpTnedbN0unfZ_89b-13hg4moTay3gTyIv3JnJl6g6xtOifffRdXavo_hw_OOpi4LCRg";
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
    builder.Services.AddScoped<IWebhookDeliveryService, WebhookDeliveryService>();
    builder.Services.AddScoped<ISesNotificationService, SesNotificationService>();

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

    builder.Services.AddControllers();
    var app = builder.Build();

    app.UseDefaultFiles();
    app.UseStaticFiles();

    app.UseHttpsRedirection();

    app.UseCors("AllowFrontend");

    app.UseAuthentication();
    app.UseAuthorization();

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
