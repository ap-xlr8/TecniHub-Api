using TECNIHUB.Application.Abstractions;
using TECNIHUB.Application.Common;

namespace TECNIHUB.Application.PostalCodes;

public sealed class PostalCodeService : IPostalCodeService
{
    private readonly IPostalCodeRepository _repository;

    public PostalCodeService(IPostalCodeRepository repository)
    {
        _repository = repository;
    }

    public async Task<Result<PostalCodeLookupDto>> LookupAsync(
        string postalCode,
        CancellationToken cancellationToken = default)
    {
        var normalized = postalCode?.Trim() ?? string.Empty;
        if (normalized.Length != 5 || !normalized.All(char.IsDigit))
        {
            return Result<PostalCodeLookupDto>.Failure(new AppError(
                ErrorType.Validation,
                "invalid_postal_code",
                "El código postal debe contener exactamente 5 dígitos."));
        }

        var entries = await _repository.FindByPostalCodeAsync(normalized, cancellationToken);
        if (entries.Count == 0)
        {
            return Result<PostalCodeLookupDto>.Success(new PostalCodeLookupDto(
                normalized,
                Found: false,
                State: null,
                Municipality: null,
                City: null,
                Neighborhoods: []));
        }

        var first = entries[0];
        var neighborhoods = entries
            .Select(e => e.Neighborhood)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return Result<PostalCodeLookupDto>.Success(new PostalCodeLookupDto(
            normalized,
            Found: true,
            State: first.State,
            Municipality: first.Municipality,
            City: string.IsNullOrWhiteSpace(first.City) ? null : first.City,
            Neighborhoods: neighborhoods));
    }
}
