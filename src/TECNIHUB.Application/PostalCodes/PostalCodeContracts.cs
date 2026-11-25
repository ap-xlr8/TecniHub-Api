using TECNIHUB.Application.Common;

namespace TECNIHUB.Application.PostalCodes;

public sealed record PostalCodeLookupDto(
    string PostalCode,
    bool Found,
    string? State,
    string? Municipality,
    string? City,
    IReadOnlyList<string> Neighborhoods);

public interface IPostalCodeService
{
    Task<Result<PostalCodeLookupDto>> LookupAsync(string postalCode, CancellationToken cancellationToken = default);
}
