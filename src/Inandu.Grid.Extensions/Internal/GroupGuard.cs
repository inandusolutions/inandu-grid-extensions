using System;

namespace Inandu.Grid.Extensions.Internal;

/// <summary>
/// Applies the column allow-list to the fields a request groups by or asks distinct values for.
/// Grouping and distinct values expose a column's values just like a filter does, so they follow
/// <see cref="InanduGridColumnConfig.CanFilter"/>: a <c>.NotFilterable()</c> column can't be grouped
/// or enumerated. A disallowed or unknown field is dropped (a stale client can't break the endpoint),
/// or throws <see cref="InanduGridRequestException"/> with
/// <see cref="InanduGridOptions.ThrowOnUnknownField"/> — the same contract as sort and filter.
/// Shared by the in-memory and EF Core entry points.
/// </summary>
internal static class GroupGuard
{
    /// <summary>
    /// Removes every <c>groupBy</c> level that isn't allowed or doesn't resolve on
    /// <paramref name="rowType"/>, together with its <c>groupKeys</c> entry so the remaining levels
    /// and keys stay aligned.
    /// </summary>
    public static void EnforceGroupBy(InanduGridRequest request, InanduGridOptions options, Type rowType)
    {
        for (var i = request.GroupBy.Count - 1; i >= 0; i--)
        {
            var problem = Check(request.GroupBy[i], options, rowType, "Grouping by", "group");
            if (problem is null)
            {
                continue;
            }

            if (options.ThrowOnUnknownField)
            {
                throw InanduGridRequestException.Single("groupBy", problem);
            }

            request.GroupBy.RemoveAt(i);
            if (i < request.GroupKeys.Count)
            {
                request.GroupKeys.RemoveAt(i);
            }
        }
    }

    /// <summary>
    /// <c>true</c> when distinct values may be listed for <paramref name="field"/>; <c>false</c> when
    /// the caller should return an empty result. Throws in strict mode instead of returning <c>false</c>.
    /// </summary>
    public static bool IsDistinctAllowed(string field, InanduGridOptions options, Type rowType)
    {
        var problem = Check(field, options, rowType, "Listing distinct values of", "distinct");
        if (problem is null)
        {
            return true;
        }

        if (options.ThrowOnUnknownField)
        {
            throw InanduGridRequestException.Single($"distinct.{field}", problem);
        }

        return false;
    }

    private static string? Check(string field, InanduGridOptions options, Type rowType, string verb, string noun)
    {
        if (string.IsNullOrWhiteSpace(field))
        {
            return $"A {noun} field must not be blank.";
        }

        if (!options.IsValueAccessAllowed(field))
        {
            return $"{verb} '{field}' is not allowed.";
        }

        if (!PropertyResolver.Exists(rowType, options.MapField(field)))
        {
            return $"Unknown {noun} field '{field}' on {rowType.Name}.";
        }

        return null;
    }
}
