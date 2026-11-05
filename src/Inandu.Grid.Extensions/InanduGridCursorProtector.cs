using System;
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace Inandu.Grid.Extensions;

/// <summary>
/// An <see cref="IInanduGridCursorProtector"/> that needs nothing beyond the .NET runtime: AES-256-GCM with a key you give it.
/// The cursor comes out encrypted (a client cannot read the sort values in it) and authenticated (any change, another sort or an
/// expired cursor is refused). Use <see cref="FromKey"/>; for a host that already runs ASP.NET Core Data Protection, the
/// <c>Inandu.Grid.Extensions.AspNetCore</c> package has <c>ProtectCursors(IDataProtectionProvider)</c> instead.
/// <para>
/// The key must be the same on every server that can answer the next page of a sequence, and it must stay secret: generate it once
/// (<c>RandomNumberGenerator.GetBytes(32)</c>) and keep it in your secret store. Changing the key invalidates the cursors in flight.
/// </para>
/// </summary>
public sealed class InanduGridCursorProtector : IInanduGridCursorProtector
{
    private const byte Version = 1;
    private const int ExpirySize = 8;
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int HeaderSize = 1 + ExpirySize;

    private readonly byte[] _key;
    private readonly TimeSpan? _lifetime;
    private readonly TimeProvider _time;

    private InanduGridCursorProtector(byte[] key, TimeSpan? lifetime, TimeProvider time)
    {
        _key = key;
        _lifetime = lifetime;
        _time = time;
    }

    /// <summary>Creates a protector from a 32-byte (256-bit) secret key.</summary>
    /// <param name="key">Exactly 32 secret bytes.</param>
    /// <param name="lifetime">How long a cursor stays valid after it was issued; <c>null</c> (the default) never expires.</param>
    /// <param name="timeProvider">Clock, for tests. Default <see cref="TimeProvider.System"/>.</param>
    public static InanduGridCursorProtector FromKey(byte[] key, TimeSpan? lifetime = null, TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (key.Length != 32)
        {
            throw new ArgumentException("The cursor key must be exactly 32 bytes (256 bits). Generate one with RandomNumberGenerator.GetBytes(32).", nameof(key));
        }

        if (lifetime is { } l && l <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(lifetime), "The lifetime must be positive.");
        }

        return new InanduGridCursorProtector((byte[])key.Clone(), lifetime, timeProvider ?? TimeProvider.System);
    }

    /// <inheritdoc />
    public string Protect(string cursor, string binding)
    {
        ArgumentNullException.ThrowIfNull(cursor);
        var plain = Encoding.UTF8.GetBytes(cursor);
        var message = new byte[HeaderSize + NonceSize + TagSize + plain.Length];
        var span = message.AsSpan();

        span[0] = Version;
        var expiry = _lifetime is { } life ? _time.GetUtcNow().Add(life).ToUnixTimeSeconds() : 0L;
        BinaryPrimitives.WriteInt64BigEndian(span.Slice(1, ExpirySize), expiry);

        var nonce = span.Slice(HeaderSize, NonceSize);
        RandomNumberGenerator.Fill(nonce);
        var tag = span.Slice(HeaderSize + NonceSize, TagSize);
        var cipher = span.Slice(HeaderSize + NonceSize + TagSize, plain.Length);

        using var aes = new AesGcm(_key, TagSize);
        aes.Encrypt(nonce, plain, cipher, tag, AssociatedData(span.Slice(0, HeaderSize), binding));
        return Convert.ToBase64String(message).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    /// <inheritdoc />
    public string? Unprotect(string protectedCursor, string binding)
    {
        if (string.IsNullOrEmpty(protectedCursor) || protectedCursor.Length > 16384)
        {
            return null;
        }

        try
        {
            var padded = protectedCursor.Replace('-', '+').Replace('_', '/');
            switch (padded.Length % 4)
            {
                case 2: padded += "=="; break;
                case 3: padded += "="; break;
            }

            var message = Convert.FromBase64String(padded);
            if (message.Length < HeaderSize + NonceSize + TagSize || message[0] != Version)
            {
                return null;
            }

            var span = message.AsSpan();
            var expiry = BinaryPrimitives.ReadInt64BigEndian(span.Slice(1, ExpirySize));
            if (expiry != 0 && _time.GetUtcNow().ToUnixTimeSeconds() > expiry)
            {
                return null;
            }

            var nonce = span.Slice(HeaderSize, NonceSize);
            var tag = span.Slice(HeaderSize + NonceSize, TagSize);
            var cipher = span.Slice(HeaderSize + NonceSize + TagSize);
            var plain = new byte[cipher.Length];

            using var aes = new AesGcm(_key, TagSize);
            aes.Decrypt(nonce, cipher, tag, plain, AssociatedData(span.Slice(0, HeaderSize), binding));
            return Encoding.UTF8.GetString(plain);
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException or ArgumentException)
        {
            return null;
        }
    }

    private static byte[] AssociatedData(ReadOnlySpan<byte> header, string binding)
    {
        var bound = Encoding.UTF8.GetBytes(binding ?? string.Empty);
        var data = new byte[header.Length + bound.Length];
        header.CopyTo(data);
        bound.CopyTo(data, header.Length);
        return data;
    }
}
