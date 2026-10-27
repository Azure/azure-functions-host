// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Google.Protobuf;
using Microsoft.Azure.WebJobs.Script.Grpc.Messages;

namespace Microsoft.Azure.WebJobs.Script.Grpc.HttpProxy;

/// <summary>
/// Validates bounded HTTP envelopes without interpreting identity selectors, API versions, or token response bodies.
/// </summary>
internal static class HttpProxyProtocol
{
    internal const uint Version = 1;
    internal const string IdentityHeader = "X-IDENTITY-HEADER";

    private static readonly HashSet<string> ExcludedHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Host", "Content-Length", "Connection", "Keep-Alive", "Proxy-Connection", "TE", "Trailer",
        "Transfer-Encoding", "Upgrade", "Proxy-Authenticate", "Proxy-Authorization"
    };

    internal static bool IsCredential(string name) =>
        string.Equals(name, IdentityHeader, StringComparison.OrdinalIgnoreCase)
        || string.Equals(name, "Authorization", StringComparison.OrdinalIgnoreCase)
        || string.Equals(name, "secret", StringComparison.OrdinalIgnoreCase);

    internal static bool IsToken(string value) => value.Length > 0 && value.All(static character =>
        char.IsAsciiLetterOrDigit(character) || "!#$%&'*+-.^_`|~".Contains(character));

    /// <summary>
    /// Restricts configuration and request paths to token-route syntax, excluding management and specialization paths.
    /// </summary>
    internal static bool IsTokenPath(string path) => path.Length > 0 && path[0] == '/'
        && path.All(static character => char.IsAsciiLetterOrDigit(character) || character is '/' or '-' or '_' or '.')
        && !path.Contains("..", StringComparison.Ordinal) && !path.Contains("//", StringComparison.Ordinal)
        && !path.StartsWith("/admin", StringComparison.OrdinalIgnoreCase)
        && !path.StartsWith("/api/specialize", StringComparison.OrdinalIgnoreCase);

    internal static void ValidateTarget(string target, string tokenPath, HttpProxyLimits limits)
    {
        var (path, _) = ParseTarget(target, limits);
        if (!string.Equals(path, tokenPath, StringComparison.OrdinalIgnoreCase))
        {
            throw new HttpProxyException(HttpProxyFailure.Types.Code.InvalidRequest);
        }
    }

    /// <summary>
    /// Splits a bounded origin-form target while leaving its raw query escaping, ordering, and duplicate keys unchanged.
    /// </summary>
    internal static (string Path, string Query) ParseTarget(string target, HttpProxyLimits limits)
    {
        int query = target.IndexOf('?');
        string path = query < 0 ? target : target[..query];
        if (!IsTokenPath(path) || target.Contains('#')
            || target.Any(static character => character is < (char)33 or > (char)126)
            || Encoding.UTF8.GetByteCount(target) > limits.MaxTargetBytes)
        {
            throw new HttpProxyException(HttpProxyFailure.Types.Code.InvalidRequest);
        }

        return (path, query < 0 ? string.Empty : target[query..]);
    }

    /// <summary>
    /// Validates header bounds and removes hop-by-hop and credential headers while preserving repeated end-to-end values.
    /// </summary>
    internal static List<HttpProxyHeader> FilterHeaders(IEnumerable<HttpProxyHeader> source, HttpProxyLimits limits)
    {
        List<HttpProxyHeader> headers = [.. source];
        long bytes = 0;
        int count = 0;
        HashSet<string> excluded = new(ExcludedHeaders, StringComparer.OrdinalIgnoreCase);
        foreach (HttpProxyHeader header in headers)
        {
            if (!IsToken(header.Name) || header.Values.Count == 0)
            {
                throw new HttpProxyException(HttpProxyFailure.Types.Code.InvalidRequest);
            }

            foreach (string value in header.Values)
            {
                if (value.Any(static character => character is '\r' or '\n' or '\0'))
                {
                    throw new HttpProxyException(HttpProxyFailure.Types.Code.InvalidRequest);
                }

                bytes += Encoding.UTF8.GetByteCount(header.Name) + Encoding.UTF8.GetByteCount(value) + 4L;
                count++;
            }

            if (string.Equals(header.Name, "Connection", StringComparison.OrdinalIgnoreCase))
            {
                foreach (string name in header.Values.SelectMany(static value => value.Split(',')))
                {
                    string nominated = name.Trim();
                    if (IsCredential(nominated))
                    {
                        throw new HttpProxyException(HttpProxyFailure.Types.Code.InvalidRequest);
                    }

                    excluded.Add(nominated);
                }
            }
        }

        if (bytes > limits.MaxHeaderBytes || count > limits.MaxHeaderCount)
        {
            throw new HttpProxyException(HttpProxyFailure.Types.Code.HeadersTooLarge);
        }

        return [.. headers.Where(header => !excluded.Contains(header.Name) && !IsCredential(header.Name))];
    }

    internal static HttpProxyHeader Header(string name, IEnumerable<string> values)
    {
        HttpProxyHeader header = new() { Name = name };
        header.Values.Add(values);
        return header;
    }

    /// <summary>
    /// Buffers an opaque body up to its limit, probing one extra byte to detect overflow without trusting Content-Length.
    /// </summary>
    internal static async Task<ByteString> ReadBodyAsync(Stream stream, int maximumBytes, bool response, CancellationToken cancellationToken)
    {
        byte[] buffer = ArrayPool<byte>.Shared.Rent(Math.Min(maximumBytes, 8192));
        try
        {
            using MemoryStream body = new();
            while (true)
            {
                int count = await stream.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, maximumBytes - body.Length + 1L)),
                    cancellationToken).ConfigureAwait(false);
                if (count == 0)
                {
                    return ByteString.CopyFrom(body.GetBuffer(), 0, (int)body.Length);
                }

                if (body.Length + count > maximumBytes)
                {
                    throw new HttpProxyException(response
                        ? HttpProxyFailure.Types.Code.ResponseTooLarge
                        : HttpProxyFailure.Types.Code.RequestTooLarge);
                }

                body.Write(buffer, 0, count);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer, clearArray: true);
        }
    }
}
