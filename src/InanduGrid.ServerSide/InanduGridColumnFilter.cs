using System.Collections.Generic;
using System.Globalization;

namespace InanduGrid.ServerSide;

/// <summary>
/// The value of one column's filter control, mirroring the core grid's
/// <c>InanduGridColumnFilterValue</c> and <c>@inandu-solutions/grid-pro</c>'s
/// <c>ColumnFilterValueLike</c>. A control usually sets just one of these fields; the field that
/// is set decides the operator (see <see cref="ToConditions"/>).
/// </summary>
public sealed class InanduGridColumnFilter
{
    /// <summary>Free-text box for the column — mapped to <see cref="FilterOperator.Contains"/>.</summary>
    public string? Text { get; set; }

    /// <summary>Numeric lower bound — mapped to <see cref="FilterOperator.GreaterThanOrEqual"/>.</summary>
    public string? Min { get; set; }

    /// <summary>Numeric upper bound — mapped to <see cref="FilterOperator.LessThanOrEqual"/>.</summary>
    public string? Max { get; set; }

    /// <summary>Date/range lower bound — mapped to <see cref="FilterOperator.GreaterThanOrEqual"/>.</summary>
    public string? From { get; set; }

    /// <summary>Date/range upper bound — mapped to <see cref="FilterOperator.LessThanOrEqual"/>.</summary>
    public string? To { get; set; }

    /// <summary>Tri-state boolean filter: <c>"true"</c> / <c>"false"</c> (anything else is ignored).</summary>
    public string? Bool { get; set; }

    /// <summary>
    /// Set-filter selection (Excel-style checklist). A non-empty list maps to
    /// <see cref="FilterOperator.In"/>; an empty list is treated as "no constraint" (a data source
    /// can't express "match nothing"), matching the grid-pro behaviour.
    /// </summary>
    public List<string>? Values { get; set; }

    /// <summary><c>true</c> when none of the fields carry an active constraint.</summary>
    public bool IsEmpty
    {
        get
        {
            if (Values is { Count: > 0 })
            {
                return false;
            }

            return string.IsNullOrEmpty(Text)
                && string.IsNullOrEmpty(Min)
                && string.IsNullOrEmpty(Max)
                && string.IsNullOrEmpty(From)
                && string.IsNullOrEmpty(To)
                && Bool is not ("true" or "false");
        }
    }

    /// <summary>
    /// Expands this control's value into zero or more normalized <see cref="FilterCondition"/> for
    /// <paramref name="field"/>. Same rules as grid-pro's <c>columnFilterToConditions</c>:
    /// <c>values</c> takes over the column entirely; otherwise <c>text→contains</c>,
    /// <c>min/from→gte</c>, <c>max/to→lte</c>, <c>bool→eq</c>.
    /// </summary>
    public IEnumerable<FilterCondition> ToConditions(string field)
    {
        if (Values is not null)
        {
            if (Values.Count > 0)
            {
                yield return new FilterCondition(field, FilterOperator.In, new List<string>(Values));
            }

            yield break;
        }

        if (!string.IsNullOrEmpty(Text))
        {
            yield return new FilterCondition(field, FilterOperator.Contains, Text);
        }

        if (IsFiniteNumber(Min))
        {
            yield return new FilterCondition(field, FilterOperator.GreaterThanOrEqual, Min);
        }

        if (IsFiniteNumber(Max))
        {
            yield return new FilterCondition(field, FilterOperator.LessThanOrEqual, Max);
        }

        if (!string.IsNullOrEmpty(From))
        {
            yield return new FilterCondition(field, FilterOperator.GreaterThanOrEqual, From);
        }

        if (!string.IsNullOrEmpty(To))
        {
            yield return new FilterCondition(field, FilterOperator.LessThanOrEqual, To);
        }

        if (Bool == "true")
        {
            yield return new FilterCondition(field, FilterOperator.Equal, true);
        }
        else if (Bool == "false")
        {
            yield return new FilterCondition(field, FilterOperator.Equal, false);
        }
    }

    private static bool IsFiniteNumber(string? value)
        => !string.IsNullOrEmpty(value)
           && double.TryParse(value, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var d)
           && !double.IsNaN(d)
           && !double.IsInfinity(d);
}
