namespace ITInventory.Web.Models;

public class PagedResult<T>
{
    public IReadOnlyList<T> Items { get; init; } = Array.Empty<T>();
    public int PageNumber { get; init; }
    public int PageSize { get; init; }
    public int TotalCount { get; init; }

    public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
}

public class PaginationInfo
{
    public int PageNumber { get; init; }
    public int PageSize { get; init; }
    public int TotalCount { get; init; }
    public int TotalPages { get; init; }
    public bool HasPrevious => PageNumber > 1;
    public bool HasNext => PageNumber < TotalPages;

    public static PaginationInfo From<T>(PagedResult<T> paged) => new()
    {
        PageNumber = paged.PageNumber,
        PageSize = paged.PageSize,
        TotalCount = paged.TotalCount,
        TotalPages = paged.TotalPages
    };
}
