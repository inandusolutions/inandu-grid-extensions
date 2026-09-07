using System;
using System.Collections.Generic;
using System.Linq;

namespace Inandu.Grid.Extensions;

/// <summary>
/// Per-column configuration, built fluently via <see cref="InanduGridOptions.Column"/>. A column
/// left unconfigured keeps the permissive defaults (any operator, sortable, and searchable only if
/// it's a <see cref="string"/> and picked up by <see cref="InanduGridOptions.SearchableFields"/>).
/// </summary>
public sealed class InanduGridColumnConfig
{
    internal InanduGridColumnConfig(string field)
    {
        Field = field;
    }

    /// <summary>The grid <c>field</c> this configures.</summary>
    public string Field { get; }

    /// <summary>CLR property path this field resolves to (overrides <see cref="InanduGridOptions.FieldMap"/>). <c>null</c> ⇒ use <see cref="Field"/>.</summary>
    public string? Path { get; internal set; }

    /// <summary>Human label — used in guard / strict-mode error messages.</summary>
    public string? Label { get; internal set; }

    /// <summary><c>null</c> ⇒ any operator; otherwise only these are honoured (others are skipped or rejected).</summary>
    public IReadOnlySet<FilterOperator>? AllowedOperators { get; internal set; }

    /// <summary>Whether the column can be filtered at all. Default <c>true</c>.</summary>
    public bool CanFilter { get; internal set; } = true;

    /// <summary>Whether the column can be sorted. Default <c>true</c>.</summary>
    public bool CanSort { get; internal set; } = true;

    /// <summary>
    /// Whether the column is part of free-text search. <c>null</c> ⇒ fall back to
    /// <see cref="InanduGridOptions.SearchableFields"/> / the string-property default; <c>true</c>/<c>false</c>
    /// force it in / out.
    /// </summary>
    public bool? Searchable { get; internal set; }

    /// <summary>Whether <paramref name="op"/> is permitted on this column.</summary>
    public bool Allows(FilterOperator op)
        => CanFilter && (AllowedOperators is null || AllowedOperators.Contains(op));
}

/// <summary>Fluent builder returned by <see cref="InanduGridOptions.Column"/>.</summary>
public sealed class InanduGridColumnBuilder
{
    private readonly InanduGridColumnConfig _config;

    internal InanduGridColumnBuilder(InanduGridColumnConfig config)
    {
        _config = config;
    }

    /// <summary>Resolve this field to a CLR property path (e.g. <c>"Customer.Name"</c>).</summary>
    public InanduGridColumnBuilder Path(string clrPath)
    {
        _config.Path = clrPath;
        return this;
    }

    /// <summary>A human label for error messages.</summary>
    public InanduGridColumnBuilder Label(string label)
    {
        _config.Label = label;
        return this;
    }

    /// <summary>Restrict filtering to these operators. No arguments ⇒ any operator (the default).</summary>
    public InanduGridColumnBuilder Filterable(params FilterOperator[] operators)
    {
        _config.CanFilter = true;
        _config.AllowedOperators = operators is { Length: > 0 } ? new HashSet<FilterOperator>(operators) : null;
        return this;
    }

    /// <summary>Disallow filtering on this column entirely.</summary>
    public InanduGridColumnBuilder NotFilterable()
    {
        _config.CanFilter = false;
        return this;
    }

    /// <summary>Allow / disallow sorting on this column.</summary>
    public InanduGridColumnBuilder Sortable(bool sortable = true)
    {
        _config.CanSort = sortable;
        return this;
    }

    /// <summary>Force this column in / out of free-text search.</summary>
    public InanduGridColumnBuilder Searchable(bool searchable = true)
    {
        _config.Searchable = searchable;
        return this;
    }
}
