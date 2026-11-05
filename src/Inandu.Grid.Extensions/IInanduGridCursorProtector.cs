namespace Inandu.Grid.Extensions;

/// <summary>
/// Turns the keyset cursor (<c>nextCursor</c> / <c>after</c>) into something a client can neither read nor forge.
/// Set <see cref="InanduGridOptions.CursorProtector"/> (or call <see cref="InanduGridOptions.ProtectCursors(byte[], System.TimeSpan?)"/>)
/// to turn protection on; see <c>docs/keyset-pagination.md</c>.
/// </summary>
public interface IInanduGridCursorProtector
{
    /// <summary>Protects <paramref name="cursor"/> (the plain cursor text), tying it to <paramref name="binding"/>.</summary>
    /// <param name="cursor">The plain cursor, as the library builds it.</param>
    /// <param name="binding">What the cursor is only valid for: the sort it was issued for (<c>"id:a|createdon:d"</c>).</param>
    string Protect(string cursor, string binding);

    /// <summary>
    /// The plain cursor, or <c>null</c> when <paramref name="protectedCursor"/> was not issued by this protector, was altered,
    /// has expired, or was issued for a different <paramref name="binding"/> (a different sort).
    /// </summary>
    string? Unprotect(string protectedCursor, string binding);
}
