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
using TECNIHUB.Application.Chat;
using TECNIHUB.Application.Common;
using TECNIHUB.Application.PostalCodes;
using TECNIHUB.Application.Profile;
using TECNIHUB.Application.Repairs;
using TECNIHUB.Application.SpareParts;
using TECNIHUB.Domain.Entities;
using TECNIHUB.Infrastructure.Persistence;

namespace TECNIHUB.IntegrationTests;

public sealed class OperationsFlowIntegrationTests : IDisposable
{
    private readonly string _databaseName = $"tecnihub-s2-{Guid.NewGuid():N}";
    private readonly CapturingEmailSender _emailSender = new();
    private readonly WebApplicationFactory<Program> _factory;

    public OperationsFlowIntegrationTests()
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
        db.PostalCodes.Add(new PostalCodeEntry
        {
            PostalCode = "42800",
            Neighborhood = "Centro",
            SettlementType = "Colonia",
            Municipality = "Tula de Allende",
            State = "Hidalgo",
            City = "Tula de Allende",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        db.SaveChanges();
    }

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task Full_operations_lifecycle_repairs_privacy_itinerary_chat_closure_and_spare_parts()
    {
        using var clientHttp = _factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        using var tech1Http = _factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        using var tech2Http = _factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });

        var clientSession = await RegisterAndLoginClientAsync(clientHttp, "cliente.s2@example.com");
        var tech1Session = await RegisterAndLoginTechnicianAsync(tech1Http, "tech1.s2@example.com");
        var tech2Session = await RegisterAndLoginTechnicianAsync(tech2Http, "tech2.s2@example.com");

        clientHttp.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", clientSession.AccessToken);
        tech1Http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tech1Session.AccessToken);
        tech2Http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tech2Session.AccessToken);

        // Profile
        var profileGet = await clientHttp.GetFromJsonAsync<UserSummaryDto>("/api/profile/me");
        Assert.NotNull(profileGet);
        var profileUpdate = await clientHttp.PutAsJsonAsync("/api/profile/me", new UpdateOwnProfileRequest("Elena", "Morales", "7731002000"));
        Assert.Equal(HttpStatusCode.OK, profileUpdate.StatusCode);

        // SEPOMEX lookup
        var cpLookup = await clientHttp.GetFromJsonAsync<PostalCodeLookupDto>("/api/postal-codes/42800");
        Assert.NotNull(cpLookup);
        Assert.True(cpLookup!.Found);
        Assert.Contains("Centro", cpLookup.Neighborhoods);

        // Client publishes repair request (mandatory 1..6 images)
        var createRepair = await clientHttp.PostAsJsonAsync("/api/repairs", new CreateRepairRequestInput(
            "Lavadora", "LG TurboDrum", "No centrifuga y marca error OE al drenar agua.",
            DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)), "Tarde (14:00 - 18:00)",
            "42800", "Hidalgo", "Tula de Allende", "Tula de Allende", "Centro",
            "Calle Zarco", "12", null, "Portón blanco", ["https://i.ibb.co/1/lavadora.jpg"]));
        Assert.Equal(HttpStatusCode.Created, createRepair.StatusCode);
        var createdRepair = await createRepair.Content.ReadFromJsonAsync<RepairDetailDto>();
        Assert.NotNull(createdRepair);
        Assert.NotNull(createdRepair!.FullAddress);

        // Technician board MUST NOT contain street or exteriorNumber
        var boardRaw = await tech1Http.GetStringAsync("/api/repairs/board");
        Assert.DoesNotContain("Calle Zarco", boardRaw);
        Assert.Contains("Centro", boardRaw);

        var openDetailForTech1 = await tech1Http.GetFromJsonAsync<RepairDetailDto>($"/api/repairs/{createdRepair.Id}");
        Assert.Null(openDetailForTech1!.FullAddress);
        Assert.Equal(Guid.Empty, openDetailForTech1.ClientUserId);
        Assert.Equal("Cliente", openDetailForTech1.ClientDisplayName);

        // Tech 1 accepts; Tech 2 gets Conflict and Forbidden on detail
        var acceptResp = await tech1Http.PostAsync($"/api/repairs/{createdRepair.Id}/accept", null);
        Assert.Equal(HttpStatusCode.OK, acceptResp.StatusCode);
        var acceptedRepair = await acceptResp.Content.ReadFromJsonAsync<RepairDetailDto>();
        Assert.NotNull(acceptedRepair!.FullAddress);
        Assert.Equal("Calle Zarco", acceptedRepair.FullAddress!.Street);
        Assert.NotNull(acceptedRepair.ConversationId);

        var doubleAccept = await tech2Http.PostAsync($"/api/repairs/{createdRepair.Id}/accept", null);
        Assert.Equal(HttpStatusCode.Conflict, doubleAccept.StatusCode);

        var tech2Detail = await tech2Http.GetAsync($"/api/repairs/{createdRepair.Id}");
        Assert.Equal(HttpStatusCode.Forbidden, tech2Detail.StatusCode);

        // Chat between client and assigned tech; tech2 is blocked (403)
        var convId = acceptedRepair.ConversationId!.Value;
        var msg1 = await clientHttp.PostAsJsonAsync($"/api/chat/conversations/{convId}/messages", new SendChatMessageInput("Hola, te espero a las 4 PM."));
        Assert.Equal(HttpStatusCode.Created, msg1.StatusCode);

        var tech2ChatBlocked = await tech2Http.GetAsync($"/api/chat/conversations/{convId}/messages");
        Assert.Equal(HttpStatusCode.Forbidden, tech2ChatBlocked.StatusCode);

        var tech1Thread = await tech1Http.GetFromJsonAsync<ChatThreadDto>($"/api/chat/conversations/{convId}/messages");
        Assert.Single(tech1Thread!.Messages);

        // Schedule + Arrival PIN + Two-step closure
        var scheduleResp = await tech1Http.PutAsJsonAsync($"/api/repairs/{createdRepair.Id}/schedule", new ScheduleRepairInput(DateTimeOffset.UtcNow.AddHours(4)));
        Assert.Equal(HttpStatusCode.OK, scheduleResp.StatusCode);

        // Skipping arrival / PIN directly from Scheduled must be blocked (409)
        var skipStartResp = await tech1Http.PostAsync($"/api/repairs/{createdRepair.Id}/start", null);
        Assert.Equal(HttpStatusCode.Conflict, skipStartResp.StatusCode);

        var arrivedResp = await tech1Http.PostAsync($"/api/repairs/{createdRepair.Id}/arrived", null);
        Assert.Equal(HttpStatusCode.OK, arrivedResp.StatusCode);

        var clientDetailForPin = await clientHttp.GetFromJsonAsync<RepairDetailDto>($"/api/repairs/{createdRepair.Id}");
        Assert.False(string.IsNullOrEmpty(clientDetailForPin!.ClientArrivalPin));

        var startResp = await tech1Http.PostAsJsonAsync($"/api/repairs/{createdRepair.Id}/start", new StartWorkInput(clientDetailForPin.ClientArrivalPin));
        Assert.Equal(HttpStatusCode.OK, startResp.StatusCode);

        var reqCloseResp = await tech1Http.PostAsJsonAsync($"/api/repairs/{createdRepair.Id}/request-closure", new RequestClosureInput("Bomba destapada y probada."));
        Assert.Equal(HttpStatusCode.OK, reqCloseResp.StatusCode);

        var confirmCloseResp = await clientHttp.PostAsync($"/api/repairs/{createdRepair.Id}/confirm-closure", null);
        Assert.Equal(HttpStatusCode.OK, confirmCloseResp.StatusCode);
        var closedRepair = await confirmCloseResp.Content.ReadFromJsonAsync<RepairDetailDto>();
        Assert.Equal("Completed", closedRepair!.Status);

        // Chat becomes read-only after completion
        var msgAfterClose = await tech1Http.PostAsJsonAsync($"/api/chat/conversations/{convId}/messages", new SendChatMessageInput("Listo"));
        Assert.Equal(HttpStatusCode.Conflict, msgAfterClose.StatusCode);

        // Spare parts (mandatory 1..6 images)
        var partCreate = await tech1Http.PostAsJsonAsync("/api/spare-parts", new UpsertSparePartInput(
            "Termostato universal", "Termostato para refrigerador doméstico dos puertas.", "Refrigeración",
            "Mabe / General Electric", "New", "Sale", 320m, "7735556677", null, null, ["https://i.ibb.co/1/termostato.jpg"]));
        Assert.Equal(HttpStatusCode.Created, partCreate.StatusCode);
        var createdPart = await partCreate.Content.ReadFromJsonAsync<SparePartDetailDto>();
        Assert.NotNull(createdPart);

        var tech2SpareChat = await tech2Http.PostAsync($"/api/chat/conversations/spare-part/{createdPart!.Id}", null);
        Assert.Equal(HttpStatusCode.OK, tech2SpareChat.StatusCode);

        var disablePart = await tech1Http.PatchAsJsonAsync($"/api/spare-parts/{createdPart.Id}/availability", new SetSparePartAvailabilityInput(false));
        Assert.Equal(HttpStatusCode.OK, disablePart.StatusCode);

        var availableParts = await tech2Http.GetFromJsonAsync<PagedResult<SparePartSummaryDto>>("/api/spare-parts?page=1&pageSize=30");
        Assert.NotNull(availableParts);
        Assert.Empty(availableParts!.Items);
        Assert.Equal(0, availableParts.TotalCount);

        // M1 regression check: soft-deleted user JWT is immediately rejected
        var deleteTech2 = await tech2Http.DeleteAsync("/api/auth/account");
        Assert.Equal(HttpStatusCode.OK, deleteTech2.StatusCode);
        var tech2BoardAfterDelete = await tech2Http.GetAsync("/api/repairs/board");
        Assert.Equal(HttpStatusCode.Unauthorized, tech2BoardAfterDelete.StatusCode);
    }

    private async Task<AuthPublicSessionResponse> RegisterAndLoginClientAsync(HttpClient http, string email)
    {
        var reg = await http.PostAsJsonAsync("/api/auth/register/client", new RegisterClientRequest("Elena", "Mora", email, "Clave#2026", null));
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
            "Jorge", "Paz", email, "Tecnico#2026", "7731230000", "Lavadoras, Refrigeradores", 5, "Tula", "Servicio Paz"));
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
