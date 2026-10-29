using Microsoft.EntityFrameworkCore;

namespace Medical.Application.Common;

/// <summary>Standard paged envelope: <c>{ items, totalCount, page, pageSize }</c>.</summary>
public sealed record PagedResult<T>(IReadOnlyList<T> Items, int TotalCount, int Page, int PageSize);

/// <summary>Common list query parameters (<c>?search=&amp;page=1&amp;pageSize=20</c>).</summary>
public class PageQuery
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;

    public string? Search { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = DefaultPageSize;

    public int NormalizedPage => Page < 1 ? 1 : Page;
    public int NormalizedPageSize => PageSize < 1 ? DefaultPageSize : Math.Min(PageSize, MaxPageSize);
    public string? NormalizedSearch => string.IsNullOrWhiteSpace(Search) ? null : Search.Trim();
}

public static class PagingExtensions
{
    /// <summary>Runs a count and a page query against an already ordered, projected query.</summary>
    public static async Task<PagedResult<T>> ToPagedAsync<T>(this IQueryable<T> query, PageQuery paging, CancellationToken ct)
    {
        var page = paging.NormalizedPage;
        var size = paging.NormalizedPageSize;
        var total = await query.CountAsync(ct);
        var items = total == 0
            ? []
            : await query.Skip((page - 1) * size).Take(size).ToListAsync(ct);
        return new PagedResult<T>(items, total, page, size);
    }
}
