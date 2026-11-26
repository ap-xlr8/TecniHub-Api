using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using TECNIHUB.API.Endpoints;
using TECNIHUB.API.Health;
using TECNIHUB.API.Hubs;
using TECNIHUB.Application.Abstractions;
using TECNIHUB.Domain.Enums;
using TECNIHUB.Infrastructure;
using TECNIHUB.Infrastructure.Security;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();
builder.Services.AddSignalR();
builder.Services.AddInfrastructure(builder.Configuration, builder.Environment.IsDevelopment());
builder.Services.AddSingleton<IRealtimeNotifier, SignalRRealtimeNotifier>();

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer();

builder.Services
    .AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<IOptions<JwtOptions>>((options, jwtOptionsAccessor) =>
    {
        var jwtOptions = jwtOptionsAccessor.Value;
        options.MapInboundClaims = true;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtOptions.Issuer,
            ValidateAudience = true,
            ValidAudience = jwtOptions.Audience,
            ValidateIssuerSigningKey = true,
            ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
            IssuerSigningKey = string.IsNullOrWhiteSpace(jwtOptions.SigningKey)
                ? null
                : new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.SigningKey)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30)
        };
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"];
                var path = context.HttpContext.Request.Path;
                if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs/realtime"))
                {
                    context.Token = accessToken;
                }

                return Task.CompletedTask;
            },
            OnTokenValidated = async context =>
            {
                var sub = context.Principal?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
                if (!Guid.TryParse(sub, out var userId))
                {
                    context.Fail("invalid_subject");
                    return;
                }

                var authRepository = context.HttpContext.RequestServices
                    .GetRequiredService<TECNIHUB.Application.Abstractions.IAuthRepository>();
                if (!await authRepository.IsUserActiveAsync(userId, context.HttpContext.RequestAborted))
                {
                    context.Fail("account_inactive");
                }
            }
        };
    });

builder.Services.AddAuthorizationBuilder()
    .AddPolicy("ClientOnly", policy => policy.RequireRole(nameof(UserRole.Client)))
    .AddPolicy("TechnicianOnly", policy => policy.RequireRole(nameof(UserRole.Technician)));

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    var isDev = builder.Environment.IsDevelopment();

    static RateLimitPartition<string> CreateFixedWindowPartition(HttpContext context, bool useUserKey, int permitLimit)
    {
        var key = useUserKey
            ? context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                ?? context.Connection.RemoteIpAddress?.ToString()
                ?? "unknown"
            : context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        return RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: key,
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = permitLimit,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            });
    }

    options.AddPolicy("AuthRateLimit", ctx => CreateFixedWindowPartition(ctx, useUserKey: false, isDev ? 120 : 30));
    options.AddPolicy("MediaUploadRateLimit", ctx => CreateFixedWindowPartition(ctx, useUserKey: true, isDev ? 60 : 20));
    options.AddPolicy("ChatRateLimit", ctx => CreateFixedWindowPartition(ctx, useUserKey: true, isDev ? 120 : 30));
});

var configuredOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
var allowedOrigins = configuredOrigins.Length > 0
    ? configuredOrigins
    : builder.Environment.IsDevelopment()
        ? ["http://localhost:4200", "https://localhost:4200", "http://127.0.0.1:4200"]
        : [];

builder.Services.AddCors(options =>
{
    options.AddPolicy("FrontendPolicy", policy =>
    {
        if (allowedOrigins.Length > 0)
        {
            policy.WithOrigins(allowedOrigins)
                .AllowAnyHeader()
                .AllowAnyMethod()
                .AllowCredentials();
        }
    });
});

var healthChecks = builder.Services.AddHealthChecks();
if (!string.IsNullOrWhiteSpace(builder.Configuration.GetConnectionString("DefaultConnection")))
{
    healthChecks.AddCheck<DatabaseHealthCheck>("postgresql");
}

var app = builder.Build();

app.UseExceptionHandler();

app.Use(async (context, next) =>
{
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["X-Frame-Options"] = "DENY";
    context.Response.Headers["Referrer-Policy"] = "no-referrer";
    context.Response.Headers["Permissions-Policy"] = "geolocation=(), microphone=(), camera=()";
    await next();
});

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}
else
{
    app.UseHsts();
    app.UseHttpsRedirection();
}

app.UseCors("FrontendPolicy");
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.MapHealthChecks("/health");
app.MapAuthEndpoints();
app.MapProfileEndpoints();
app.MapPostalCodeEndpoints();
app.MapMediaEndpoints();
app.MapRepairRequestEndpoints();
app.MapSparePartEndpoints();
app.MapChatEndpoints();
app.MapPublicProfileEndpoints();
app.MapNotificationEndpoints();
app.MapHub<RealtimeHub>("/hubs/realtime");

app.Run();

public partial class Program { }
