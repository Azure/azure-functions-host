// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Threading;
using Microsoft.Azure.WebJobs.Script.WebHost.Properties;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics;

/// <summary>
/// Keeps request log properties unescaped and shares the formatted JSON payload across providers.
/// </summary>
internal sealed class ExecutedHttpRequestLogState(
    string? mS_ActivityId, string? identities, int statusCode, long duration, string? route)
    : IReadOnlyList<KeyValuePair<string, object?>>
{
    private static readonly CompositeFormat MessageFormat = CompositeFormat.Parse(
        Resources.ExecutedHttpRequest
            .Replace("{mS_ActivityId}", "{0}", StringComparison.Ordinal)
            .Replace("{identities}", "{1}", StringComparison.Ordinal)
            .Replace("{statusCode}", "{2}", StringComparison.Ordinal)
            .Replace("{duration}", "{3}", StringComparison.Ordinal)
            .Replace("{route}", "{4}", StringComparison.Ordinal));

    // Box once rather than on each provider's enumeration and formatting.
    private readonly object _statusCode = statusCode;
    private readonly object _duration = duration;
    private string? _formattedMessage;

    /// <inheritdoc/>
    public int Count => 6;

    /// <inheritdoc/>
    public KeyValuePair<string, object?> this[int index] => index switch
    {
        0 => new(nameof(mS_ActivityId), mS_ActivityId),
        1 => new(nameof(identities), identities),
        2 => new(nameof(statusCode), _statusCode),
        3 => new(nameof(duration), _duration),
        4 => new(nameof(route), route),
        5 => new("{OriginalFormat}", Resources.ExecutedHttpRequest),
        _ => throw new ArgumentOutOfRangeException(nameof(index)),
    };

    /// <inheritdoc/>
    public override string ToString()
    {
        string? message = Volatile.Read(ref _formattedMessage);
        if (message is not null)
        {
            return message;
        }

        message = string.Format(
            CultureInfo.InvariantCulture,
            MessageFormat,
            JavaScriptEncoder.Default.Encode(mS_ActivityId ?? "(null)"),
            JavaScriptEncoder.Default.Encode(identities ?? "(null)"),
            _statusCode,
            _duration,
            JavaScriptEncoder.Default.Encode(route ?? "(null)"));

        return Interlocked.CompareExchange(ref _formattedMessage, message, null) ?? message;
    }

    /// <inheritdoc/>
    public IEnumerator<KeyValuePair<string, object?>> GetEnumerator()
    {
        for (int i = 0; i < Count; i++)
        {
            yield return this[i];
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
