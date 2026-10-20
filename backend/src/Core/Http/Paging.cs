using Microsoft.EntityFrameworkCore;

namespace Farm360.Core.Http;

/// <summary>Page-number paging with a hard maximum page size. Bind it with <c>[AsParameters]</c>.</summary>
public sealed record PageRequest(int Page = 1, int PageSize = PageRequest.DefaultPageSize)
{
    public const int DefaultPageSize = 25;
    public const int MaxPageSize = 100;

    /// <summary>The page, at least 1.</summary>
    public int Number => Math.Max(1, Page);

    /// <summary>The page size, between 1 and <see cref="MaxPageSize"/>: a client can't ask for everything at once.</summary>
    public int Size => Math.Clamp(PageSize, 1, MaxPageSize);
}

/// <summary>The one response shape for lists.</summary>
public sealed record Page<T>(IReadOnlyList<T> Items, int PageNumber, int PageSize, int TotalCount)
{
    public int TotalPages => (int)Math.Ceiling(TotalCount / (double)PageSize);

    public bool HasNext => PageNumber < TotalPages;
}

public static class PagingExtensions
{
    /// <summary>Counts, then reads one page. Order the query first, or the pages are not stable.</summary>
    public static async Task<Page<T>> ToPageAsync<T>(this IQueryable<T> query, PageRequest request, CancellationToken cancellationToken = default)
    {
        var total = await query.CountAsync(cancellationToken);
        var items = await query.Skip((request.Number - 1) * request.Size).Take(request.Size).ToListAsync(cancellationToken);
        return new Page<T>(items, request.Number, request.Size, total);
    }

    public static Page<TResult> Map<T, TResult>(this Page<T> page, Func<T, TResult> map) =>
        new([.. page.Items.Select(map)], page.PageNumber, page.PageSize, page.TotalCount);
}
