using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace Inandu.Grid.Extensions.Tests;

/// <summary>
/// Parses the query strings emitted by <c>@inandu-solutions/grid-react</c>'s
/// <c>buildInanduGridQuery</c> and checks they mean what the React side intends. The fixture
/// (<c>Contract/server-queries.json</c>) is shared with that repo, which asserts the other half
/// (grid state → query string) — keep the two copies identical.
/// </summary>
public class ReactClientContractTests
{
    public static TheoryData<string> CaseNames()
    {
        var data = new TheoryData<string>();
        foreach (var c in Load().RootElement.GetProperty("cases").EnumerateArray())
        {
            data.Add(c.GetProperty("name").GetString()!);
        }
        return data;
    }

    private static JsonDocument Load() =>
        JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Contract", "server-queries.json")));

    [Theory]
    [MemberData(nameof(CaseNames))]
    public void Query_string_from_the_react_client_parses_as_intended(string name)
    {
        using var doc = Load();
        var c = doc.RootElement.GetProperty("cases").EnumerateArray().Single(e => e.GetProperty("name").GetString() == name);
        var expected = c.GetProperty("parsed");

        var request = InanduGridRequest.Parse(c.GetProperty("queryString").GetString());

        Assert.Equal(expected.GetProperty("page").GetInt32(), request.Page);
        Assert.Equal(expected.GetProperty("pageSize").GetInt32(), request.PageSize);
        Assert.Equal(expected.GetProperty("q").GetString(), request.Query);

        var sort = request.Sort.Select(s => (s.Direction == SortDirection.Descending ? "-" : "") + s.Field);
        Assert.Equal(expected.GetProperty("sort").EnumerateArray().Select(e => e.GetString()), sort);

        var conditions = request.AllConditions()
            .Select(x => $"{x.Field} {x.Operator.ToToken()} {Render(x.Value)}")
            .OrderBy(x => x, StringComparer.Ordinal);
        var wanted = expected.GetProperty("conditions").EnumerateArray().Select(e => e.GetString()!).OrderBy(x => x, StringComparer.Ordinal);
        Assert.Equal(wanted, conditions);
    }

    private static string Render(object? value) =>
        value is IEnumerable items && value is not string
            ? string.Join(",", items.Cast<object?>())
            : Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? "";
}
