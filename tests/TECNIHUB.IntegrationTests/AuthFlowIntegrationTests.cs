using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TECNIHUB.Application.Abstractions;
using TECNIHUB.Application.Auth.DTOs;
using TECNIHUB.Infrastructure.Persistence;

namespace TECNIHUB.IntegrationTests;

public sealed class AuthFlowIntegrationTests : IDisposable
{
    private readonly string _databaseName = $"tecnihub-test-{Guid.NewGuid():N}";
    private readonly CapturingEmailSender _emailSender = new();
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public AuthFlowIntegrationTests()
    {
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:DefaultConnection"] = string.Empty,
                    ["Jwt:Issuer"] = "TECNIHUB.API",
                    ["Jwt:Audience"] = "TECNIHUB.Frontend",
                    ["Jwt:SigningKey"] = "IntegrationTestSigningKey_AtLeast32BytesLong_2026!",
                    ["Jwt:AccessTokenMinutes"] = "15"
                });
            });

            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IDbContextOptionsConfiguration<TecniHubDbContext>>();
                services.RemoveAll<DbContextOptions<TecniHubDbContext>>();
                services.RemoveAll<TecniHubDbContext>();
                services.AddDbContext<TecniHubDbContext>(options =>
                    options.UseInMemoryDatabase(_databaseName));

                services.RemoveAll<IEmailSender>();
                services.AddSingleton<IEmailSender>(_emailSender);
            });
        });

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TecniHubDbContext>();
            db.Database.EnsureCreated();
        }

        _client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            HandleCookies = true
        });
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    [Fact]
    public async Task Client_full_auth_lifecycle_enforces_roles_refresh_logout_and_audit_logs()
    {
        var register = await _client.PostAsJsonAsync(
            "/api/auth/register/client",
            new RegisterClientRequest("Valeria", "Núñez", "valeria@example.com", "Clave#2026", null));
        Assert.Equal(HttpStatusCode.Created, register.StatusCode);

        var code = Assert.Single(_emailSender.VerificationCodes).Code;
        var verify = await _client.PostAsJsonAsync(
            "/api/auth/verify-email",
            new VerifyEmailRequest("valeria@example.com", code));
        Assert.Equal(HttpStatusCode.OK, verify.StatusCode);

        var loginResponse = await _client.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest("valeria@example.com", "Clave#2026"));
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);
        Assert.True(loginResponse.Headers.TryGetValues("Set-Cookie", out var setCookies));
        Assert.Contains(setCookies, c => c.Contains("tecnihub_rt=", StringComparison.Ordinal) && c.Contains("httponly", StringComparison.OrdinalIgnoreCase));

        var session = await loginResponse.Content.ReadFromJsonAsync<AuthPublicSessionResponse>();
        Assert.NotNull(session);
        Assert.Equal("Client", session!.User.Role);

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);

        var meResponse = await _client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.OK, meResponse.StatusCode);

        var clientEndpoint = await _client.GetAsync("/api/client/summary");
        Assert.Equal(HttpStatusCode.OK, clientEndpoint.StatusCode);

        var technicianEndpoint = await _client.GetAsync("/api/technician/summary");
        Assert.Equal(HttpStatusCode.Forbidden, technicianEndpoint.StatusCode);

        var refreshResponse = await _client.PostAsync("/api/auth/refresh", null);
        Assert.Equal(HttpStatusCode.OK, refreshResponse.StatusCode);
        var refreshed = await refreshResponse.Content.ReadFromJsonAsync<AuthPublicSessionResponse>();
        Assert.NotNull(refreshed);
        Assert.False(string.IsNullOrWhiteSpace(refreshed!.AccessToken));

        var logoutResponse = await _client.PostAsync("/api/auth/logout", null);
        Assert.Equal(HttpStatusCode.OK, logoutResponse.StatusCode);

        var refreshAfterLogout = await _client.PostAsync("/api/auth/refresh", null);
        Assert.Equal(HttpStatusCode.Unauthorized, refreshAfterLogout.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TecniHubDbContext>();
        var actions = await db.AuditLogs.Select(a => a.Action).ToListAsync();
        Assert.Contains("UserRegisteredClient", actions);
        Assert.Contains("EmailVerified", actions);
        Assert.Contains("UserLoggedIn", actions);
        Assert.Contains("SessionRefreshed", actions);
        Assert.Contains("UserLoggedOut", actions);
    }

    [Fact]
    public async Task Technician_registration_password_reset_and_gradual_deletion_work_end_to_end()
    {
        var register = await _client.PostAsJsonAsync(
            "/api/auth/register/technician",
            new RegisterTechnicianRequest(
                "Roberto",
                "Salinas",
                "roberto.tec@example.com",
                "Tecnico#2026",
                "5511223344",
                "Lavadoras, Secadoras",
                8,
                "Zona Norte",
                "ElectroSoluciones"));
        Assert.Equal(HttpStatusCode.Created, register.StatusCode);

        var verifyCode = _emailSender.VerificationCodes.Last().Code;
        var verify = await _client.PostAsJsonAsync(
            "/api/auth/verify-email",
            new VerifyEmailRequest("roberto.tec@example.com", verifyCode));
        Assert.Equal(HttpStatusCode.OK, verify.StatusCode);

        var resetReq = await _client.PostAsJsonAsync(
            "/api/auth/password-reset/request",
            new RequestPasswordResetRequest("roberto.tec@example.com"));
        Assert.Equal(HttpStatusCode.OK, resetReq.StatusCode);

        var resetCode = _emailSender.PasswordResetCodes.Last().Code;
        var resetConfirm = await _client.PostAsJsonAsync(
            "/api/auth/password-reset/confirm",
            new ConfirmPasswordResetRequest("roberto.tec@example.com", resetCode, "NuevaTec#2026"));
        Assert.Equal(HttpStatusCode.OK, resetConfirm.StatusCode);

        var loginResponse = await _client.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest("roberto.tec@example.com", "NuevaTec#2026"));
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);

        var session = await loginResponse.Content.ReadFromJsonAsync<AuthPublicSessionResponse>();
        Assert.Equal("Technician", session!.User.Role);

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);

        var techSummary = await _client.GetAsync("/api/technician/summary");
        Assert.Equal(HttpStatusCode.OK, techSummary.StatusCode);

        var clientSummary = await _client.GetAsync("/api/client/summary");
        Assert.Equal(HttpStatusCode.Forbidden, clientSummary.StatusCode);

        var deleteResponse = await _client.DeleteAsync("/api/auth/account");
        Assert.Equal(HttpStatusCode.OK, deleteResponse.StatusCode);

        _client.DefaultRequestHeaders.Authorization = null;
        var loginAfterDeletion = await _client.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest("roberto.tec@example.com", "NuevaTec#2026"));
        Assert.Equal(HttpStatusCode.Unauthorized, loginAfterDeletion.StatusCode);
    }

    private sealed class CapturingEmailSender : IEmailSender
    {
        public List<(string Email, string Code)> VerificationCodes { get; } = [];
        public List<(string Email, string Code)> PasswordResetCodes { get; } = [];

        public Task<bool> SendVerificationCodeAsync(string toEmail, string recipientName, string code, CancellationToken cancellationToken = default)
        {
            VerificationCodes.Add((toEmail, code));
            return Task.FromResult(true);
        }

        public Task<bool> SendPasswordResetCodeAsync(string toEmail, string recipientName, string code, CancellationToken cancellationToken = default)
        {
            PasswordResetCodes.Add((toEmail, code));
            return Task.FromResult(true);
        }
    }
}
