namespace TECNIHUB.Application.Common;

public enum ErrorType
{
    Validation = 1,
    Conflict = 2,
    Unauthorized = 3,
    Forbidden = 4,
    NotFound = 5,
    TooManyRequests = 6,
    ServiceUnavailable = 7
}

public sealed record AppError(
    ErrorType Type,
    string Code,
    string Message,
    IReadOnlyList<string>? Details = null);

public sealed class Result<T>
{
    private Result(T? value, AppError? error)
    {
        Value = value;
        Error = error;
    }

    public bool IsSuccess => Error is null;
    public T? Value { get; }
    public AppError? Error { get; }

    public static Result<T> Success(T value) => new(value, null);
    public static Result<T> Failure(AppError error) => new(default, error);
}

public sealed record RequestMetadata(string? IpAddress, string? UserAgent);
