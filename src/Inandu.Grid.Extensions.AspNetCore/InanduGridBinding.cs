using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Primitives;

namespace Inandu.Grid.Extensions.AspNetCore;

/// <summary>
/// Builds an <see cref="InanduGridRequest"/> from an <see cref="HttpRequest"/> — the query string
/// (<c>page</c>, <c>sort=-field</c>, <c>{field}_{op}</c>, <c>q</c>, <c>filter</c>, <c>groupBy</c>…),
/// or, for a JSON POST, the request body. Use it directly in a minimal API; MVC controllers can use
/// <see cref="FromInanduGridAttribute"/> or <see cref="InanduGridMvcExtensions.AddInanduGridModelBinding"/>.
/// </summary>
public static class InanduGridBinding
{
    /// <summary>
    /// The largest JSON body <see cref="FromHttpRequestAsync(HttpRequest, CancellationToken)"/> reads: 128 KB. A grid
    /// request is a handful of short strings; the host's own default (30 MB on Kestrel) is far more than it needs.
    /// </summary>
    public const long DefaultMaxBodyBytes = 131_072;

    /// <summary>Binds from <paramref name="request"/> — JSON body when it is a JSON POST/PUT, else the query string. A body over <see cref="DefaultMaxBodyBytes"/> is refused with 413.</summary>
    public static ValueTask<InanduGridRequest> FromHttpRequestAsync(HttpRequest request, CancellationToken cancellationToken = default)
        => FromHttpRequestAsync(request, DefaultMaxBodyBytes, cancellationToken);

    /// <summary>
    /// Binds like <see cref="FromHttpRequestAsync(HttpRequest, CancellationToken)"/> with your own cap on the JSON body
    /// (<paramref name="maxBodyBytes"/>, or 0 to leave the host's limit alone). A larger body throws a
    /// <see cref="BadHttpRequestException"/> with status 413, which ASP.NET Core turns into a 413 response.
    /// </summary>
    public static async ValueTask<InanduGridRequest> FromHttpRequestAsync(HttpRequest request, long maxBodyBytes, CancellationToken cancellationToken = default)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        if (HasJsonBody(request))
        {
            if (maxBodyBytes > 0)
            {
                if (request.ContentLength > maxBodyBytes)
                {
                    throw new BadHttpRequestException($"The request body is larger than the {maxBodyBytes} bytes a grid request may have.", StatusCodes.Status413PayloadTooLarge);
                }

                // chunked bodies have no Content-Length: let the server stop reading at the cap instead
                var feature = request.HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>();
                if (feature is { IsReadOnly: false } && (feature.MaxRequestBodySize is null || feature.MaxRequestBodySize > maxBodyBytes))
                {
                    feature.MaxRequestBodySize = maxBodyBytes;
                }
            }

            try
            {
                var fromBody = await request.ReadFromJsonAsync<InanduGridRequest>(cancellationToken).ConfigureAwait(false);
                if (fromBody is not null)
                {
                    return fromBody;
                }
            }
            catch (JsonException)
            {
                // fall through to the query string
            }
        }

        return FromQuery(request.Query);
    }

    /// <summary>Binds from the query string only (synchronous).</summary>
    public static InanduGridRequest FromHttpRequest(HttpRequest request)
        => FromQuery((request ?? throw new ArgumentNullException(nameof(request))).Query);

    /// <summary>Binds from an <see cref="IQueryCollection"/>.</summary>
    public static InanduGridRequest FromQuery(IQueryCollection query)
    {
        if (query is null)
        {
            throw new ArgumentNullException(nameof(query));
        }

        return InanduGridRequest.Parse(Flatten(query));
    }

    private static IEnumerable<KeyValuePair<string, string?>> Flatten(IQueryCollection query)
    {
        foreach (var pair in query)
        {
            foreach (var value in (StringValues)pair.Value)
            {
                yield return new KeyValuePair<string, string?>(pair.Key, value);
            }
        }
    }

    private static bool HasJsonBody(HttpRequest request)
    {
        if (!HttpMethods.IsPost(request.Method) && !HttpMethods.IsPut(request.Method) && !HttpMethods.IsPatch(request.Method))
        {
            return false;
        }

        var contentType = request.ContentType;
        return !string.IsNullOrEmpty(contentType)
               && contentType.Contains("json", StringComparison.OrdinalIgnoreCase);
    }
}
