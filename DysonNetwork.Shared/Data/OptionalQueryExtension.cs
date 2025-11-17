using Microsoft.EntityFrameworkCore.Query;

namespace DysonNetwork.Shared.Data;

public static class OptionalQueryExtensions
{
    public static IQueryable<T> If<T>(
        this IQueryable<T> source,
        bool condition,
        Func<IQueryable<T>, IQueryable<T>> transform
    )
    {
        return condition ? transform(source) : source;
    }

    public static IQueryable<T> If<T, TP>(
        this IIncludableQueryable<T, TP> source,
        bool condition,
        Func<IIncludableQueryable<T, TP>, IQueryable<T>> transform
    )
        where T : class
    {
        return condition ? transform(source) : source;
    }

    public static IQueryable<T> If<T, TP>(
        this IIncludableQueryable<T, IEnumerable<TP>> source,
        bool condition,
        Func<IIncludableQueryable<T, IEnumerable<TP>>, IQueryable<T>> transform
    )
        where T : class
    {
        return condition ? transform(source) : source;
    }
}