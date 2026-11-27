namespace TECNIHUB.Application.Common;

public sealed record PagedResult<T>(
    IReadOnlyList<T> Items,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages,
    int? ActiveCount = null,
    int? SecondaryCount = null,
    int? PendingClosureCount = null)
{
    public const int DefaultClientPageSize = 12;
    public const int DefaultBoardPageSize = 30;
    public const int DefaultItineraryPageSize = 12;
    public const int DefaultSparePartsPageSize = 30;
    public const int MaxPageSize = 50;

    public static (int Page, int PageSize) Normalize(int? page, int? pageSize, int defaultPageSize)
    {
        var safePage = page is > 0 ? page.Value : 1;
        var safeSize = pageSize is > 0 ? Math.Min(pageSize.Value, MaxPageSize) : defaultPageSize;
        return (safePage, safeSize);
    }

    public static PagedResult<T> Create(
        IReadOnlyList<T> items,
        int page,
        int pageSize,
        int totalCount,
        int? activeCount = null,
        int? secondaryCount = null,
        int? pendingClosureCount = null)
    {
        var totalPages = totalCount == 0 ? 1 : (int)Math.Ceiling(totalCount / (double)pageSize);
        var effectivePage = Math.Min(page, totalPages);
        return new PagedResult<T>(
            items,
            effectivePage,
            pageSize,
            totalCount,
            totalPages,
            activeCount,
            secondaryCount,
            pendingClosureCount);
    }
}
