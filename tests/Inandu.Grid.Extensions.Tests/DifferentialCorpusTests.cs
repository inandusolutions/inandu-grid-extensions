using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace Inandu.Grid.Extensions.Tests;

/// <summary>
/// The differential corpus (<c>Contract/diff-corpus.json</c>) is run through grid-core, grid-angular, grid-react and grid-vue on
/// the client and through this library on the server. Every one of them must return the same ids, in the same order, for the
/// same request: this is the server half. The file is shared with those repos (their CI compares their copy with this one).
/// </summary>
public class DifferentialCorpusTests
{
    public sealed class CorpusRow
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public int? Qty { get; set; }
        public decimal Price { get; set; }
        public DateTime When { get; set; }
        public bool Active { get; set; }
        public string? Tag { get; set; }
    }

    private static JsonDocument Load() =>
        JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Contract", "diff-corpus.json")));

    public static TheoryData<string> CaseNames()
    {
        var data = new TheoryData<string>();
        using var doc = Load();
        foreach (var c in doc.RootElement.GetProperty("cases").EnumerateArray())
        {
            data.Add(c.GetProperty("name").GetString()!);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(CaseNames))]
    public void The_server_returns_the_ids_every_client_returns(string name)
    {
        using var doc = Load();
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var rows = doc.RootElement.GetProperty("rows").Deserialize<List<CorpusRow>>(options)!;
        var c = doc.RootElement.GetProperty("cases").EnumerateArray().Single(e => e.GetProperty("name").GetString() == name);
        var expected = c.GetProperty("expectedIds").EnumerateArray().Select(e => e.GetInt32()).ToList();

        var page = rows.ToInanduGrid("?pageSize=500&" + c.GetProperty("server").GetString(), o => o.ThrowOnUnknownField = true);

        Assert.Equal(expected, page.Data.Select(r => r.Id).ToList());
    }
}
