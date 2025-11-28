using Email.Server.Authentication;
using Email.Server.Data;
using Email.Server.DTOs.Requests;
using Email.Server.Mapping;
using Email.Server.Services.Implementations;
using Email.Server.Services.Interfaces;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Serilog;
using System.Text;

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

    // Configure Identity
    builder.Services.AddIdentity<ApplicationUser, IdentityRole>(options =>
    {
        // Password settings
        options.Password.RequireDigit = true;
        options.Password.RequireLowercase = true;
        options.Password.RequireUppercase = true;
        options.Password.RequireNonAlphanumeric = false;
        options.Password.RequiredLength = 6;

        // User settings
        options.User.RequireUniqueEmail = true;

        // Sign-in settings
        options.SignIn.RequireConfirmedEmail = false; // Set to true if you want email confirmation
    })
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddDefaultTokenProviders();

    // Configure JWT Authentication
    var jwtSettings = builder.Configuration.GetSection("JwtSettings");
    var secretKey = jwtSettings["SecretKey"] ?? throw new InvalidOperationException("JWT SecretKey is not configured");

    builder.Services.AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
    })
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtSettings["Issuer"],
            ValidAudience = jwtSettings["Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey)),
            ClockSkew = TimeSpan.Zero
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

    // Add HttpClientFactory for webhook confirmations and deliveries
    builder.Services.AddHttpClient();
    builder.Services.AddHttpClient("WebhookClient", client =>
    {
        client.DefaultRequestHeaders.Add("Accept", "application/json");
    });

    // Register Background Services
    builder.Services.AddHostedService<SesProvisioningRetryService>();
    builder.Services.AddHostedService<ScheduledEmailService>();
    builder.Services.AddHostedService<Email.Server.Services.Background.WebhookDeliveryBackgroundService>();

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