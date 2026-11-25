using System.Security.Claims;
using TECNIHUB.Application.Common;
using TECNIHUB.Domain.Enums;

namespace TECNIHUB.API.Endpoints;

internal static class EndpointHelpers
{
    public static bool TryGetUserId(ClaimsPrincipal user, out Guid userId) =>
        Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out userId);

    public static bool TryGetUserRole(ClaimsPrincipal user, out UserRole role)
    {
        var roleClaim = user.FindFirstValue(ClaimTypes.Role);
        return Enum.TryParse(roleClaim, ignoreCase: true, out role) && Enum.IsDefined(role);
    }

    public static RequestMetadata BuildMetadata(HttpContext httpContext) => new(
        Truncate(httpContext.Connection.RemoteIpAddress?.ToString(), 64),
        Truncate(httpContext.Request.Headers.UserAgent.ToString(), 300));

    public static IResult ToProblemResult(AppError error)
    {
        var statusCode = error.Type switch
        {
            ErrorType.Validation => StatusCodes.Status400BadRequest,
            ErrorType.Conflict => StatusCodes.Status409Conflict,
            ErrorType.Unauthorized => StatusCodes.Status401Unauthorized,
            ErrorType.Forbidden => StatusCodes.Status403Forbidden,
            ErrorType.NotFound => StatusCodes.Status404NotFound,
            ErrorType.TooManyRequests => StatusCodes.Status429TooManyRequests,
            ErrorType.ServiceUnavailable => StatusCodes.Status503ServiceUnavailable,
            _ => StatusCodes.Status400BadRequest
        };

        var extensions = new Dictionary<string, object?>
        {
            ["code"] = error.Code
        };

        if (error.Details is { Count: > 0 })
        {
            extensions["errors"] = error.Details;
        }

        return Results.Problem(
            title: error.Message,
            detail: error.Message,
            statusCode: statusCode,
            extensions: extensions);
    }

    private static string? Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }
}
