using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using TECNIHUB.Application.Abstractions;
using TECNIHUB.Application.Media;
using TECNIHUB.Domain.Rules;

namespace TECNIHUB.API.Endpoints;

public static class MediaEndpoints
{
    private const long MaxMultipartRequestBytes = 6 * 1024 * 1024;

    public static IEndpointRouteBuilder MapMediaEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/media/images", UploadImageAsync)
            .RequireAuthorization()
            .RequireRateLimiting("MediaUploadRateLimit")
            .DisableAntiforgery()
            .WithMetadata(new RequestSizeLimitAttribute(MaxMultipartRequestBytes))
            .WithTags("Media")
            .WithName("UploadImage")
            .Produces<UploadedImageDto>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        return app;
    }

    private static async Task<IResult> UploadImageAsync(
        IFormFile? file,
        ClaimsPrincipal user,
        IImageStorageService storageService,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (!EndpointHelpers.TryGetUserId(user, out var userId))
        {
            return Results.Unauthorized();
        }

        if (file is null || file.Length <= 0)
        {
            return Results.Problem(
                title: "Debes seleccionar una imagen para subir.",
                detail: "Debes seleccionar una imagen para subir.",
                statusCode: StatusCodes.Status400BadRequest,
                extensions: new Dictionary<string, object?> { ["code"] = "image_required" });
        }

        if (file.Length > ImageSignatureValidator.MaxSizeBytes)
        {
            return Results.Problem(
                title: "La imagen supera el tamaño máximo permitido de 5 MB.",
                detail: "La imagen supera el tamaño máximo permitido de 5 MB.",
                statusCode: StatusCodes.Status400BadRequest,
                extensions: new Dictionary<string, object?> { ["code"] = "image_too_large" });
        }

        var role = user.FindFirstValue(ClaimTypes.Role) ?? "Unknown";
        await using var stream = file.OpenReadStream();
        var result = await storageService.UploadImageAsync(
            stream,
            file.Length,
            userId,
            role,
            EndpointHelpers.BuildMetadata(httpContext),
            cancellationToken);

        return result.IsSuccess
            ? Results.Created(result.Value!, new UploadedImageDto(result.Value!))
            : EndpointHelpers.ToProblemResult(result.Error!);
    }
}
