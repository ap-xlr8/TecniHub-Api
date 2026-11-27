using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using TECNIHUB.Application.Auth.DTOs;
using TECNIHUB.Application.Common;
using TECNIHUB.Domain.Enums;

namespace TECNIHUB.API.Endpoints;

public static class AuthEndpoints
{
    public const string RefreshTokenCookieName = "tecnihub_rt";

    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth").WithTags("Auth").RequireRateLimiting("AuthRateLimit");

        group.MapPost("/register/client", RegisterClientAsync)
            .WithName("RegisterClient")
            .Produces<RegistrationResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/register/technician", RegisterTechnicianAsync)
            .WithName("RegisterTechnician")
            .Produces<RegistrationResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/verify-email", VerifyEmailAsync)
            .WithName("VerifyEmail")
            .Produces<GenericMessageResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        group.MapPost("/resend-verification", ResendVerificationAsync)
            .WithName("ResendVerification")
            .Produces<GenericMessageResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        group.MapPost("/login", LoginAsync)
            .WithName("Login")
            .Produces<AuthPublicSessionResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapPost("/refresh", RefreshAsync)
            .RequireRateLimiting("RefreshRateLimit")
            .WithName("RefreshSession")
            .Produces<AuthPublicSessionResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        group.MapPost("/logout", LogoutAsync)
            .WithName("Logout")
            .Produces<GenericMessageResponse>(StatusCodes.Status200OK);

        group.MapPost("/password-reset/request", RequestPasswordResetAsync)
            .WithName("RequestPasswordReset")
            .Produces<GenericMessageResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapPost("/password-reset/confirm", ConfirmPasswordResetAsync)
            .WithName("ConfirmPasswordReset")
            .Produces<GenericMessageResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        group.MapGet("/me", GetCurrentUserAsync)
            .RequireAuthorization()
            .RequireRateLimiting("RefreshRateLimit")
            .WithName("GetCurrentUser")
            .Produces<UserSummaryDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized);

        group.MapDelete("/account", RequestAccountDeletionAsync)
            .RequireAuthorization()
            .WithName("RequestAccountDeletion")
            .Produces<GenericMessageResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized);

        app.MapGet("/api/client/summary", (ClaimsPrincipal user) =>
                Results.Ok(new
                {
                    role = nameof(UserRole.Client),
                    message = "Panel de cliente autorizado.",
                    userId = user.FindFirstValue(ClaimTypes.NameIdentifier)
                }))
            .RequireAuthorization("ClientOnly")
            .WithTags("Client")
            .WithName("GetClientSummary");

        app.MapGet("/api/technician/summary", (ClaimsPrincipal user) =>
                Results.Ok(new
                {
                    role = nameof(UserRole.Technician),
                    message = "Panel de técnico autorizado.",
                    userId = user.FindFirstValue(ClaimTypes.NameIdentifier)
                }))
            .RequireAuthorization("TechnicianOnly")
            .WithTags("Technician")
            .WithName("GetTechnicianSummary");

        return app;
    }

    private static async Task<IResult> RegisterClientAsync(
        RegisterClientRequest request,
        IAuthService authService,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var result = await authService.RegisterClientAsync(request, EndpointHelpers.BuildMetadata(httpContext), cancellationToken);
        return result.IsSuccess
            ? Results.Created("/api/auth/me", result.Value)
            : EndpointHelpers.ToProblemResult(result.Error!);
    }

    private static async Task<IResult> RegisterTechnicianAsync(
        RegisterTechnicianRequest request,
        IAuthService authService,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var result = await authService.RegisterTechnicianAsync(request, EndpointHelpers.BuildMetadata(httpContext), cancellationToken);
        return result.IsSuccess
            ? Results.Created("/api/auth/me", result.Value)
            : EndpointHelpers.ToProblemResult(result.Error!);
    }

    private static async Task<IResult> VerifyEmailAsync(
        VerifyEmailRequest request,
        IAuthService authService,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var result = await authService.VerifyEmailAsync(request, EndpointHelpers.BuildMetadata(httpContext), cancellationToken);
        return result.IsSuccess ? Results.Ok(result.Value) : EndpointHelpers.ToProblemResult(result.Error!);
    }

    private static async Task<IResult> ResendVerificationAsync(
        ResendVerificationRequest request,
        IAuthService authService,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var result = await authService.ResendVerificationCodeAsync(request, EndpointHelpers.BuildMetadata(httpContext), cancellationToken);
        return result.IsSuccess ? Results.Ok(result.Value) : EndpointHelpers.ToProblemResult(result.Error!);
    }

    private static async Task<IResult> LoginAsync(
        LoginRequest request,
        IAuthService authService,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var result = await authService.LoginAsync(request, EndpointHelpers.BuildMetadata(httpContext), cancellationToken);
        if (!result.IsSuccess)
        {
            return EndpointHelpers.ToProblemResult(result.Error!);
        }

        var session = result.Value!;
        SetRefreshTokenCookie(httpContext, session.RefreshToken, session.RefreshTokenExpiresAt);

        return Results.Ok(new AuthPublicSessionResponse(
            session.AccessToken,
            session.AccessTokenExpiresAt,
            session.User));
    }

    private static async Task<IResult> RefreshAsync(
        IAuthService authService,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var rawToken = httpContext.Request.Cookies[RefreshTokenCookieName];
        var result = await authService.RefreshSessionAsync(rawToken, EndpointHelpers.BuildMetadata(httpContext), cancellationToken);
        if (!result.IsSuccess)
        {
            ClearRefreshTokenCookie(httpContext);
            return EndpointHelpers.ToProblemResult(result.Error!);
        }

        var session = result.Value!;
        SetRefreshTokenCookie(httpContext, session.RefreshToken, session.RefreshTokenExpiresAt);

        return Results.Ok(new AuthPublicSessionResponse(
            session.AccessToken,
            session.AccessTokenExpiresAt,
            session.User));
    }

    private static async Task<IResult> LogoutAsync(
        IAuthService authService,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var rawToken = httpContext.Request.Cookies[RefreshTokenCookieName];
        Guid? userId = Guid.TryParse(httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier), out var parsed)
            ? parsed
            : null;
        var role = httpContext.User.FindFirstValue(ClaimTypes.Role);

        var result = await authService.LogoutAsync(rawToken, userId, role, EndpointHelpers.BuildMetadata(httpContext), cancellationToken);
        ClearRefreshTokenCookie(httpContext);

        return Results.Ok(result.Value);
    }

    private static async Task<IResult> RequestPasswordResetAsync(
        RequestPasswordResetRequest request,
        IAuthService authService,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var result = await authService.RequestPasswordResetAsync(request, EndpointHelpers.BuildMetadata(httpContext), cancellationToken);
        return result.IsSuccess ? Results.Ok(result.Value) : EndpointHelpers.ToProblemResult(result.Error!);
    }

    private static async Task<IResult> ConfirmPasswordResetAsync(
        ConfirmPasswordResetRequest request,
        IAuthService authService,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var result = await authService.ConfirmPasswordResetAsync(request, EndpointHelpers.BuildMetadata(httpContext), cancellationToken);
        return result.IsSuccess ? Results.Ok(result.Value) : EndpointHelpers.ToProblemResult(result.Error!);
    }

    private static async Task<IResult> GetCurrentUserAsync(
        ClaimsPrincipal user,
        IAuthService authService,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
        {
            return Results.Unauthorized();
        }

        var result = await authService.GetCurrentUserAsync(userId, cancellationToken);
        return result.IsSuccess ? Results.Ok(result.Value) : EndpointHelpers.ToProblemResult(result.Error!);
    }

    private static async Task<IResult> RequestAccountDeletionAsync(
        ClaimsPrincipal user,
        IAuthService authService,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
        {
            return Results.Unauthorized();
        }

        var result = await authService.RequestAccountDeletionAsync(userId, EndpointHelpers.BuildMetadata(httpContext), cancellationToken);
        if (!result.IsSuccess)
        {
            return EndpointHelpers.ToProblemResult(result.Error!);
        }

        ClearRefreshTokenCookie(httpContext);
        return Results.Ok(result.Value);
    }

    private static void SetRefreshTokenCookie(
        HttpContext httpContext,
        string token,
        DateTimeOffset expiresAt)
    {
        httpContext.Response.Cookies.Append(
            RefreshTokenCookieName,
            token,
            BuildCookieOptions(expiresAt));
    }

    private static void ClearRefreshTokenCookie(HttpContext httpContext)
    {
        httpContext.Response.Cookies.Delete(
            RefreshTokenCookieName,
            BuildCookieOptions(DateTimeOffset.UtcNow.AddDays(-1)));
    }

    private static CookieOptions BuildCookieOptions(DateTimeOffset expiresAt) => new()
    {
        HttpOnly = true,
        Secure = true,
        SameSite = SameSiteMode.None,
        Path = "/api/auth",
        Expires = expiresAt,
        IsEssential = true
    };
}
