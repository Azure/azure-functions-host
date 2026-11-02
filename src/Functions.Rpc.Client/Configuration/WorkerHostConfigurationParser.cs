// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

#nullable enable

using System;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using Microsoft.Azure.WebJobs.Script;
using Microsoft.Azure.WebJobs.Script.Configuration;
using Microsoft.Azure.WebJobs.Script.Grpc.Messages;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Azure.Functions.Rpc.Client;

/// <summary>
/// Parses Host configuration delivered by a worker without publishing configuration or performing file I/O.
/// </summary>
internal sealed partial class WorkerHostConfigurationParser(
    IConfiguration configuration,
    ILogger<WorkerHostConfigurationParser> logger) : IWorkerHostConfigurationParser
{
    internal const int MaximumContentLength = 65_536;

    private static readonly string ContentTooLargeReason = FormattableString.Invariant($"The Host configuration exceeds the {MaximumContentLength:N0}-byte limit.");
    private static readonly Encoding Utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <inheritdoc />
    [return: NotNullIfNotNull(nameof(delivery))]
    public IConfigurationSource? Parse(HostConfiguration? delivery)
    {
        if (delivery is null)
        {
            return null;
        }

        if (delivery.Content.Length > MaximumContentLength)
        {
            throw Reject(ContentTooLargeReason);
        }

        if (delivery.Status != HostConfiguration.Types.Status.Available && !delivery.Content.IsEmpty)
        {
            throw Reject("The Host configuration delivery has content inconsistent with its status.");
        }

        string? content = delivery.Status switch
        {
            HostConfiguration.Types.Status.Available => Decode(delivery.Content.Span),
            HostConfiguration.Types.Status.Missing => null,
            HostConfiguration.Types.Status.Error => throw Reject("The worker could not produce Host configuration."),
            HostConfiguration.Types.Status.TooLarge => throw Reject(ContentTooLargeReason),
            _ => throw Reject("The Host configuration delivery status is unspecified or unsupported."),
        };
        string? profile = configuration[$"{ConfigurationSectionNames.JobHost}:configurationProfile"];

        try
        {
            return HostConfigurationSnapshot.Parse(content, profile);
        }
        catch (HostConfigurationException)
        {
            throw Reject("The Host configuration must be a JSON object with a supported configuration profile.");
        }
    }

    private string Decode(ReadOnlySpan<byte> content)
    {
        if (content is [0xEF, 0xBB, 0xBF, ..])
        {
            throw Reject("The Host configuration must be UTF-8 JSON without a byte order mark.");
        }

        try
        {
            return Utf8.GetString(content);
        }
        catch (DecoderFallbackException)
        {
            throw Reject("The Host configuration contains invalid UTF-8 text.");
        }
    }

    private HostConfigurationException Reject(string reason)
    {
        // Never attach configuration values or parser exceptions.
        Log.HostConfigurationRejected(logger, reason);

        return new(reason);
    }

    private static partial class Log
    {
        [LoggerMessage(0, LogLevel.Warning, "Host configuration was rejected. {Reason}")]
        public static partial void HostConfigurationRejected(ILogger logger, string reason);
    }
}
