using System;
using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;

namespace Inandu.Grid.Extensions.AspNetCore;

/// <summary>An <see cref="IInanduGridCursorProtector"/> over ASP.NET Core Data Protection: the cursor is encrypted with the app's key ring and expires.</summary>
public sealed class DataProtectionCursorProtector : IInanduGridCursorProtector
{
    private const char Separator = '\u001f';
    private readonly ITimeLimitedDataProtector _protector;
    private readonly TimeSpan _lifetime;

    /// <summary>Creates a protector with the given purpose string and cursor lifetime.</summary>
    public DataProtectionCursorProtector(IDataProtectionProvider provider, TimeSpan lifetime)
    {
        ArgumentNullException.ThrowIfNull(provider);
        if (lifetime <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(lifetime), "The lifetime must be positive.");
        }

        _protector = provider.CreateProtector("Inandu.Grid.Extensions.Cursor.v1").ToTimeLimitedDataProtector();
        _lifetime = lifetime;
    }

    /// <inheritdoc />
    public string Protect(string cursor, string binding)
        => _protector.Protect(binding + Separator + cursor, _lifetime);

    /// <inheritdoc />
    public string? Unprotect(string protectedCursor, string binding)
    {
        try
        {
            var plain = _protector.Unprotect(protectedCursor);
            var cut = plain.IndexOf(Separator);
            return cut >= 0 && string.Equals(plain.AsSpan(0, cut).ToString(), binding, StringComparison.Ordinal)
                ? plain[(cut + 1)..]
                : null;
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException or ArgumentException)
        {
            return null; // altered, expired, or from another key ring
        }
    }
}

/// <summary>Turns on cursor protection through ASP.NET Core Data Protection.</summary>
public static class InanduGridCursorProtectionExtensions
{
    /// <summary>
    /// Protects keyset cursors with the host's Data Protection key ring (<c>builder.Services.AddDataProtection()</c>), so a client
    /// cannot read or forge them. Cursors expire after <paramref name="lifetime"/> (default 1 hour) and only work for the sort they were
    /// issued for. With several servers, share the key ring (<c>PersistKeysTo…</c>) so any of them can answer the next page.
    /// </summary>
    public static InanduGridOptions ProtectCursors(this InanduGridOptions options, IDataProtectionProvider provider, TimeSpan? lifetime = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.CursorProtector = new DataProtectionCursorProtector(provider, lifetime ?? TimeSpan.FromHours(1));
        return options;
    }
}
