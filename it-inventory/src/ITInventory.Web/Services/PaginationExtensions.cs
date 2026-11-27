using ITInventory.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace ITInventory.Web.Services;

public static class PaginationExtensions
{
    public const int DefaultPageSize = 25;

    public static async Task<PagedResult<T>> ToPagedResultAsync<T>(this IQueryable<T> query, int pageNumber, int pageSize = DefaultPageSize)
    {
        if (pageNumber < 1) pageNumber = 1;

        var totalCount = await query.CountAsync();
        var items = await query.Skip((pageNumber - 1) * pageSize).Take(pageSize).ToListAsync();

        return new PagedResult<T>
        {
            Items = items,
            PageNumber = pageNumber,
            PageSize = pageSize,
            TotalCount = totalCount
        };
    }
}
