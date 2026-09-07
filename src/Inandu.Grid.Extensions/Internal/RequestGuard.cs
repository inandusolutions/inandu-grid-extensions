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
