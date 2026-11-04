using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace Inandu.Grid.Extensions.Internal;

/// <summary>
/// Enforces <see cref="InanduGridLimits"/> on an incoming request — either trimming it down to the
/// caps (<see cref="InanduGridLimitMode.Trim"/>, the default) or throwing
/// <see cref="InanduGridRequestException"/> (<see cref="InanduGridLimitMode.Reject"/>). Runs once,
/// at the top of every entry point.
/// </summary>
internal static class RequestGuard
{
    public static void Enforce(InanduGridRequest request, InanduGridOptions options)
    {
        var limits = options.Limits;
        var reject = options.OnLimitExceeded == InanduGridLimitMode.Reject;
        var errors = reject ? new Dictionary<string, string[]>() : null;

        // How long each string is, before anything is decoded or compiled: the counts below say how many.
        CapStringSizes(request, limits, reject, errors);

        // sort columns
        if (request.Sort.Count > limits.MaxSortColumns)
        {
            Fail(errors, "sort", $"at most {limits.MaxSortColumns} sort columns are allowed ({request.Sort.Count} given).");
            if (!reject)
            {
                while (request.Sort.Count > limits.MaxSortColumns)
                {
                    request.Sort.RemoveAt(request.Sort.Count - 1);
                }
            }
        }

        // free-text search and the raw advanced-filter JSON
        if (request.Query is { } queryText && queryText.Length > limits.MaxQueryTextLength)
        {
            Fail(errors, "q", $"the search text is {queryText.Length} characters; the limit is {limits.MaxQueryTextLength}.");
            if (!reject)
            {
                request.Query = queryText.Substring(0, Math.Max(0, limits.MaxQueryTextLength));
            }
        }

        if (request.Filter is { } filterJson && filterJson.Length > limits.MaxFilterLength)
        {
            Fail(errors, "filter", $"the advanced filter is {filterJson.Length} characters; the limit is {limits.MaxFilterLength}.");
            if (!reject)
            {
                request.Filter = null;
            }
        }

        // flat conditions
        if (request.Conditions.Count > limits.MaxConditions)
        {
            Fail(errors, "filter", $"at most {limits.MaxConditions} filter conditions are allowed ({request.Conditions.Count} given).");
            if (!reject)
            {
                while (request.Conditions.Count > limits.MaxConditions)
                {
                    request.Conditions.RemoveAt(request.Conditions.Count - 1);
                }
            }
        }

        // `in` list lengths
        for (var i = 0; i < request.Conditions.Count; i++)
        {
            var c = request.Conditions[i];
            if (c.Operator != FilterOperator.In)
            {
                continue;
            }

            var items = (c.Value as IEnumerable)?.Cast<object?>().ToList();
            if (items is { Count: > 0 } && items.Count > limits.MaxInListItems)
            {
                Fail(errors, $"filter.{c.Field}", $"the `in` list for '{c.Field}' has {items.Count} items; the limit is {limits.MaxInListItems}.");
                if (!reject)
                {
                    request.Conditions[i] = new FilterCondition(c.Field, FilterOperator.In, items.Take(limits.MaxInListItems).ToList());
                }
            }
        }

        foreach (var pair in request.ColumnFilters)
        {
            if (pair.Value.Values is { Count: > 0 } values && values.Count > limits.MaxInListItems)
            {
                Fail(errors, $"filter.{pair.Key}", $"the set filter for '{pair.Key}' has {values.Count} values; the limit is {limits.MaxInListItems}.");
                if (!reject)
                {
                    values.RemoveRange(limits.MaxInListItems, values.Count - limits.MaxInListItems);
                }
            }
        }

        // advanced filter tree
        if (request.AdvancedFilter is { } tree)
        {
            var (nodes, depth) = Measure(tree, 1);
            if (nodes > limits.MaxAdvancedFilterNodes)
            {
                Fail(errors, "filter", $"the advanced filter has {nodes} nodes; the limit is {limits.MaxAdvancedFilterNodes}.");
                if (!reject)
                {
                    request.AdvancedFilter = null;
                }
            }
            else if (depth > limits.MaxAdvancedFilterDepth)
            {
                Fail(errors, "filter", $"the advanced filter nests {depth} levels deep; the limit is {limits.MaxAdvancedFilterDepth}.");
                if (!reject)
                {
                    request.AdvancedFilter = null;
                }
            }
        }

        // groupBy levels
        if (request.GroupBy.Count > limits.MaxGroupByLevels)
        {
            Fail(errors, "groupBy", $"at most {limits.MaxGroupByLevels} group levels are allowed ({request.GroupBy.Count} given).");
            if (!reject)
            {
                while (request.GroupBy.Count > limits.MaxGroupByLevels)
                {
                    request.GroupBy.RemoveAt(request.GroupBy.Count - 1);
                }
            }
        }

        // aggregations
        if (request.Aggregations.Count > limits.MaxAggregations)
        {
            Fail(errors, "aggregate", $"at most {limits.MaxAggregations} aggregations are allowed ({request.Aggregations.Count} given).");
            if (!reject)
            {
                while (request.Aggregations.Count > limits.MaxAggregations)
                {
                    request.Aggregations.RemoveAt(request.Aggregations.Count - 1);
                }
            }
        }

        if (reject && errors!.Count > 0)
        {
            throw new InanduGridRequestException("The grid request exceeds one or more limits.", errors);
        }
    }

    private static void CapStringSizes(InanduGridRequest request, InanduGridLimits limits, bool reject, Dictionary<string, string[]>? errors)
    {
        if (request.After is { } after && after.Length > limits.MaxCursorLength)
        {
            Fail(errors, "after", $"the cursor is {after.Length} characters; the limit is {limits.MaxCursorLength}.");
            if (!reject)
            {
                request.After = null; // as invalid as any cursor that does not decode: the first page
            }
        }

        for (var i = request.Sort.Count - 1; i >= 0; i--)
        {
            if (request.Sort[i]?.Field is { } sortField && sortField.Length > limits.MaxFieldNameLength)
            {
                Fail(errors, "sort", $"a sort field name is {sortField.Length} characters; the limit is {limits.MaxFieldNameLength}.");
                request.Sort.RemoveAt(i);
            }
        }

        for (var i = request.GroupBy.Count - 1; i >= 0; i--)
        {
            if (request.GroupBy[i] is { } groupField && groupField.Length > limits.MaxFieldNameLength)
            {
                Fail(errors, "groupBy", $"a group-by field name is {groupField.Length} characters; the limit is {limits.MaxFieldNameLength}.");
                request.GroupBy.RemoveAt(i);
            }
        }

        for (var i = request.GroupKeys.Count - 1; i >= 0; i--)
        {
            if (request.GroupKeys[i] is { } key && key.Length > limits.MaxValueLength)
            {
                Fail(errors, "groupKeys", $"a group key is {key.Length} characters; the limit is {limits.MaxValueLength}.");
                request.GroupKeys.RemoveAt(i);
            }
        }

        for (var i = request.Conditions.Count - 1; i >= 0; i--)
        {
            var c = request.Conditions[i];
            if (c.Field.Length > limits.MaxFieldNameLength)
            {
                Fail(errors, "filter", $"a filter field name is {c.Field.Length} characters; the limit is {limits.MaxFieldNameLength}.");
                request.Conditions.RemoveAt(i);
                continue;
            }

            if (c.Value is string text)
            {
                if (text.Length > limits.MaxValueLength)
                {
                    Fail(errors, $"filter.{c.Field}", $"the value for '{c.Field}' is {text.Length} characters; the limit is {limits.MaxValueLength}.");
                    request.Conditions.RemoveAt(i);
                }
            }
            else if (c.Value is IEnumerable items && c.Value is not string)
            {
                var all = items.Cast<object?>().ToList();
                var kept = all.Where(v => v is not string s || s.Length <= limits.MaxValueLength).ToList();
                if (kept.Count != all.Count)
                {
                    Fail(errors, $"filter.{c.Field}", $"{all.Count - kept.Count} value(s) for '{c.Field}' are longer than {limits.MaxValueLength} characters.");
                    request.Conditions[i] = new FilterCondition(c.Field, c.Operator, kept);
                }
            }
        }

        foreach (var key in request.ColumnFilters.Keys.ToList())
        {
            var filter = request.ColumnFilters[key];
            var tooLongName = key.Length > limits.MaxFieldNameLength;
            var tooLongValue = filter is not null && (Long(filter.Text) || Long(filter.Min) || Long(filter.Max) || Long(filter.From) || Long(filter.To) || Long(filter.Bool)
                || (filter.Values?.Any(v => Long(v)) ?? false));
            if (tooLongName || tooLongValue)
            {
                Fail(errors, $"filter.{key}", tooLongName
                    ? $"a column filter name is {key.Length} characters; the limit is {limits.MaxFieldNameLength}."
                    : $"a value of the '{key}' filter is longer than {limits.MaxValueLength} characters.");
                request.ColumnFilters.Remove(key);
            }
        }

        bool Long(string? value) => value is not null && value.Length > limits.MaxValueLength;
    }

    private static (int Nodes, int Depth) Measure(AdvancedFilterNode node, int depth)
    {
        if (node is not AdvancedFilterGroup group)
        {
            return (1, depth);
        }

        var nodes = 1;
        var maxDepth = depth;
        foreach (var child in group.Children)
        {
            var (n, d) = Measure(child, depth + 1);
            nodes += n;
            if (d > maxDepth)
            {
                maxDepth = d;
            }
        }

        return (nodes, maxDepth);
    }

    private static void Fail(Dictionary<string, string[]>? errors, string key, string message)
    {
        if (errors is null)
        {
            return;
        }

        errors[key] = errors.TryGetValue(key, out var existing)
            ? existing.Append(message).ToArray()
            : new[] { message };
    }
}
