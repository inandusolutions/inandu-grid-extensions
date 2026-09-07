namespace Inandu.Grid.Extensions.Internal;

/// <summary>Projection target for the dynamic <c>GroupBy(...).Select(g =&gt; new { g.Key, Count })</c> — a concrete
/// generic type so EF Core can materialize it.</summary>
internal sealed class GroupCount<TKey>
{
    public TKey Key { get; set; } = default!;

    public int Count { get; set; }
}
