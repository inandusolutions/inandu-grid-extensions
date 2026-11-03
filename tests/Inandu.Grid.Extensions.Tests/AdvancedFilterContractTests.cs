using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace Inandu.Grid.Extensions.Tests;

/// <summary>
/// Parses the <c>filter</c> trees emitted by <c>@inandu-solutions/grid-pro</c>'s
/// <c>advancedQueryToRestParams</c> and checks they mean what the client intends. The fixture
/// (<c>Contract/advanced-filter-queries.json</c>) is shared with the grid-pro repos (Angular, React
/// and Vue), which assert the other half (query tree → <c>filter</c> param) — keep the copies
/// identical. Also pins the operator vocabulary: the enum must match the client's list one to one.
/// </summary>
public class AdvancedFilterContractTests
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
        JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Contract", "advanced-filter-queries.json")));

    [Fact]
    public void Operator_vocabulary_matches_the_client_one_to_one()
    {
        using var doc = Load();
        var client = doc.RootElement.GetProperty("operators").EnumerateArray().Select(e => e.GetString()!)
            .OrderBy(x => x, StringComparer.Ordinal);
        var server = Enum.GetNames<AdvancedFilterOperator>().Select(ToToken).OrderBy(x => x, StringComparer.Ordinal);

        Assert.Equal(client, server);
    }

    [Theory]
    [MemberData(nameof(CaseNames))]
    public void Filter_from_the_client_parses_as_intended(string name)
    {
        using var doc = Load();
        var c = doc.RootElement.GetProperty("cases").EnumerateArray().Single(e => e.GetProperty("name").GetString() == name);
        var filter = c.GetProperty("filter");
        var parsed = c.GetProperty("parsed");

        if (filter.ValueKind == JsonValueKind.Null)
        {
            Assert.Equal(JsonValueKind.Null, parsed.ValueKind); // the client sends no `filter` param at all
            return;
        }

        var tree = AdvancedFilterJson.Parse(filter.GetRawText());

        Assert.NotNull(tree);
        Assert.Equal(parsed.GetString(), Render(tree!));
    }

    private static string ToToken(string enumName) => char.ToLowerInvariant(enumName[0]) + enumName[1..];

    private static string Render(AdvancedFilterNode node) => node switch
    {
        AdvancedFilterGroup g => $"{ToToken(g.Combinator.ToString())}({string.Join(", ", g.Children.Select(Render))})",
        AdvancedFilterCondition c => RenderCondition(c),
        _ => throw new InvalidOperationException("Unknown node " + node.GetType().Name),
    };

    private static string RenderCondition(AdvancedFilterCondition c)
    {
        var op = ToToken(c.Operator.ToString());
        return c.Operator switch
        {
            AdvancedFilterOperator.IsTrue or AdvancedFilterOperator.IsFalse
                or AdvancedFilterOperator.IsEmpty or AdvancedFilterOperator.IsNotEmpty => $"{c.Field} {op}",
            AdvancedFilterOperator.Between => $"{c.Field} {op} {Text(c.Value)}..{Text(c.Value2)}",
            _ => $"{c.Field} {op} {Text(c.Value)}",
        };
    }

    private static string Text(object? value) => value switch
    {
        bool b => b ? "true" : "false",
        null => "",
        _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? "",
    };
}
