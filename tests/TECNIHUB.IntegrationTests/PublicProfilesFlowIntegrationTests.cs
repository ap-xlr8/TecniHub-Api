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
using TECNIHUB.Application.Profile;
using TECNIHUB.Application.Repairs;
using TECNIHUB.Application.Reviews;
using TECNIHUB.Infrastructure.Persistence;

namespace TECNIHUB.IntegrationTests;

public sealed class PublicProfilesFlowIntegrationTests : IDisposable
{
    private readonly string _databaseName = $"tecnihub-profiles-{Guid.NewGuid():N}";
    private readonly CapturingEmailSender _emailSender = new();
    private readonly WebApplicationFactory<Program> _factory;

    public PublicProfilesFlowIntegrationTests()
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

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TecniHubDbContext>();
        db.Database.EnsureCreated();
    }

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task Public_profiles_and_optional_reviews_enforce_privacy_and_completion_rules()
    {
        using var clientHttp = _factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        using var tech1Http = _factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        using var tech2Http = _factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });

        var clientSession = await RegisterAndLoginClientAsync(clientHttp, "cliente.pub@example.com");
        var tech1Session = await RegisterAndLoginTechnicianAsync(tech1Http, "tech1.pub@example.com");
        var tech2Session = await RegisterAndLoginTechnicianAsync(tech2Http, "tech2.pub@example.com");

        clientHttp.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", clientSession.AccessToken);
        tech1Http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tech1Session.AccessToken);
        tech2Http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tech2Session.AccessToken);

        // Client sets optional public region
        var updateProfile = await clientHttp.PutAsJsonAsync(
            "/api/profile/me",
            new UpdateOwnProfileRequest("Sofía", "Luna", "7735550000", PublicRegion: "Tepeji del Río, Hidalgo"));
        Assert.Equal(HttpStatusCode.OK, updateProfile.StatusCode);

        // Technician public profile before any reviews: averageRating MUST be null and email/phone MUST NOT leak
        var techProfileRaw = await clientHttp.GetStringAsync($"/api/profiles/technicians/{tech1Session.User.Id}");
        Assert.DoesNotContain("tech1.pub@example.com", techProfileRaw);
        Assert.DoesNotContain("7731230000", techProfileRaw);
        var techProfileBefore = await clientHttp.GetFromJsonAsync<PublicTechnicianProfileDto>($"/api/profiles/technicians/{tech1Session.User.Id}");
        Assert.NotNull(techProfileBefore);
        Assert.Null(techProfileBefore!.AverageRating);
        Assert.Equal(0, techProfileBefore.ReviewCount);

        // Before assignment, neither technician can inspect the client's public profile
        var unauthorizedClientProfile = await tech1Http.GetAsync($"/api/profiles/clients/{clientSession.User.Id}");
        Assert.Equal(HttpStatusCode.Forbidden, unauthorizedClientProfile.StatusCode);

        // Client creates repair -> Tech 1 accepts -> Start -> Request closure -> Client confirms (without review)
        var createRepair = await clientHttp.PostAsJsonAsync("/api/repairs", new CreateRepairRequestInput(
            "Microondas", "LG NeoChef", "No calienta los alimentos aunque el plato gira normalmente.",
            DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)), "Mañana (09:00 - 13:00)",
            "42855", "Hidalgo", "Tepeji del Río de Ocampo", "Tepeji del Río de Ocampo", "Tlaxinacalpan",
            "Calle Privada Secreta", "99", null, "Casa azul", ["https://i.ibb.co/1/microondas.jpg"]));
        Assert.Equal(HttpStatusCode.Created, createRepair.StatusCode);
        var repair = (await createRepair.Content.ReadFromJsonAsync<RepairDetailDto>())!;

        Assert.Equal(HttpStatusCode.OK, (await tech1Http.PostAsync($"/api/repairs/{repair.Id}/accept", null)).StatusCode);

        // Assigned tech1 CAN inspect client's limited public profile, without email/phone/street
        var clientProfileRaw = await tech1Http.GetStringAsync($"/api/profiles/clients/{clientSession.User.Id}");
        Assert.DoesNotContain("cliente.pub@example.com", clientProfileRaw);
        Assert.DoesNotContain("7735550000", clientProfileRaw);
        Assert.DoesNotContain("Calle Privada Secreta", clientProfileRaw);
        var clientProfile = await tech1Http.GetFromJsonAsync<PublicClientProfileDto>($"/api/profiles/clients/{clientSession.User.Id}");
        Assert.Equal("Tepeji del Río, Hidalgo", clientProfile!.PublicRegion);

        // Unassigned tech2 is still blocked (403)
        Assert.Equal(HttpStatusCode.Forbidden, (await tech2Http.GetAsync($"/api/profiles/clients/{clientSession.User.Id}")).StatusCode);

        // Review before completion is rejected (409)
        var earlyReview = await clientHttp.PostAsJsonAsync($"/api/repairs/{repair.Id}/review", new CreateServiceReviewInput(5, "Aún no acaba"));
        Assert.Equal(HttpStatusCode.Conflict, earlyReview.StatusCode);

        // Complete service (arrival + PIN verification) without requiring review
        Assert.Equal(HttpStatusCode.OK, (await tech1Http.PostAsync($"/api/repairs/{repair.Id}/arrived", null)).StatusCode);
        var pin = (await clientHttp.GetFromJsonAsync<RepairDetailDto>($"/api/repairs/{repair.Id}"))!.ClientArrivalPin;
        Assert.Equal(HttpStatusCode.OK, (await tech1Http.PostAsJsonAsync($"/api/repairs/{repair.Id}/start", new StartWorkInput(pin))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await tech1Http.PostAsJsonAsync($"/api/repairs/{repair.Id}/request-closure", new RequestClosureInput("Magnetrón cambiado"))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await clientHttp.PostAsync($"/api/repairs/{repair.Id}/confirm-closure", null)).StatusCode);

        // Optional review after completion succeeds once and rejects duplicates
        var reviewResp = await clientHttp.PostAsJsonAsync($"/api/repairs/{repair.Id}/review", new CreateServiceReviewInput(5, "Muy profesional y limpio."));
        Assert.Equal(HttpStatusCode.Created, reviewResp.StatusCode);

        var dupReview = await clientHttp.PostAsJsonAsync($"/api/repairs/{repair.Id}/review", new CreateServiceReviewInput(4, "Duplicada"));
        Assert.Equal(HttpStatusCode.Conflict, dupReview.StatusCode);

        var techProfileAfter = await clientHttp.GetFromJsonAsync<PublicTechnicianProfileDto>($"/api/profiles/technicians/{tech1Session.User.Id}");
        Assert.Equal(1, techProfileAfter!.CompletedServicesCount);
        Assert.Equal(1, techProfileAfter.ReviewCount);
        Assert.Equal(5.0m, techProfileAfter.AverageRating);
        Assert.Single(techProfileAfter.Reviews);
    }

    private async Task<AuthPublicSessionResponse> RegisterAndLoginClientAsync(HttpClient http, string email)
    {
        var reg = await http.PostAsJsonAsync("/api/auth/register/client", new RegisterClientRequest("Sofía", "Luna", email, "Clave#2026", null));
        Assert.Equal(HttpStatusCode.Created, reg.StatusCode);
        var code = _emailSender.VerificationCodes.Last(c => c.Email == email).Code;
        Assert.Equal(HttpStatusCode.OK, (await http.PostAsJsonAsync("/api/auth/verify-email", new VerifyEmailRequest(email, code))).StatusCode);
        var login = await http.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, "Clave#2026"));
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        return (await login.Content.ReadFromJsonAsync<AuthPublicSessionResponse>())!;
    }

    private async Task<AuthPublicSessionResponse> RegisterAndLoginTechnicianAsync(HttpClient http, string email)
    {
        var reg = await http.PostAsJsonAsync("/api/auth/register/technician", new RegisterTechnicianRequest(
            "Andrés", "Reyes", email, "Tecnico#2026", "7731230000", "Microondas, Estufas", 6, "Tepeji", "ElectroReyes"));
        Assert.Equal(HttpStatusCode.Created, reg.StatusCode);
        var code = _emailSender.VerificationCodes.Last(c => c.Email == email).Code;
        Assert.Equal(HttpStatusCode.OK, (await http.PostAsJsonAsync("/api/auth/verify-email", new VerifyEmailRequest(email, code))).StatusCode);
        var login = await http.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, "Tecnico#2026"));
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        return (await login.Content.ReadFromJsonAsync<AuthPublicSessionResponse>())!;
    }

    private sealed class CapturingEmailSender : IEmailSender
    {
        public List<(string Email, string Code)> VerificationCodes { get; } = [];
        public Task<bool> SendVerificationCodeAsync(string toEmail, string recipientName, string code, CancellationToken cancellationToken = default)
        {
            VerificationCodes.Add((toEmail, code));
            return Task.FromResult(true);
        }
        public Task<bool> SendPasswordResetCodeAsync(string toEmail, string recipientName, string code, CancellationToken cancellationToken = default) => Task.FromResult(true);
    }
}
