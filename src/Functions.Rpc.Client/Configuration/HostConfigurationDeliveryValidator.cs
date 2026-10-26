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
using Microsoft.Extensions.Options;

namespace Azure.Functions.Rpc.Client;

/// <summary>
/// Validates Host configuration delivered by a worker without publishing configuration or performing file I/O.
/// </summary>
internal sealed partial class HostConfigurationDeliveryValidator(
    IConfiguration configuration,
    IOptionsMonitor<ScriptApplicationHostOptions> applicationOptions,
    ILogger<HostConfigurationDeliveryValidator> logger) : IHostConfigurationDeliveryValidator
{
    internal const int MaximumContentLength = 65_536;

    private static readonly string ContentTooLargeReason = FormattableString.Invariant($"The Host configuration exceeds the {MaximumContentLength:N0}-byte limit.");
    private static readonly Encoding Utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
    private static readonly Encoding Utf16LittleEndian = new UnicodeEncoding(bigEndian: false, byteOrderMark: false, throwOnInvalidBytes: true);
    private static readonly Encoding Utf16BigEndian = new UnicodeEncoding(bigEndian: true, byteOrderMark: false, throwOnInvalidBytes: true);
    private static readonly Encoding Utf32LittleEndian = new UTF32Encoding(bigEndian: false, byteOrderMark: false, throwOnInvalidCharacters: true);
    private static readonly Encoding Utf32BigEndian = new UTF32Encoding(bigEndian: true, byteOrderMark: false, throwOnInvalidCharacters: true);

    /// <inheritdoc />
    [return: NotNullIfNotNull(nameof(delivery))]
    public IConfigurationSource? Validate(HostConfiguration? delivery)
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

        try
        {
            string? content = delivery.Status switch
            {
                HostConfiguration.Types.Status.Available => Decode(delivery.Content.Span),
                HostConfiguration.Types.Status.Missing => null,
                HostConfiguration.Types.Status.ReadError => throw Reject("The worker could not read host.json."),
                HostConfiguration.Types.Status.TooLarge => throw Reject(ContentTooLargeReason),
                _ => throw Reject("The Host configuration delivery status is unspecified or unsupported."),
            };

            HostJsonFileConfigurationOptions options = new(configuration, applicationOptions.CurrentValue);

            return HostConfigurationSnapshot.Parse(content, options);
        }
        catch (DecoderFallbackException)
        {
            throw Reject("The Host configuration contains invalid encoded text.");
        }
        catch (HostConfigurationException)
        {
            throw Reject("The Host configuration is not valid JSON with a supported schema version and configuration profile.");
        }
    }

    private static string Decode(ReadOnlySpan<byte> content)
    {
        (Encoding encoding, int preambleLength) = content switch
        {
            [0x00, 0x00, 0xFE, 0xFF, ..] => (Utf32BigEndian, 4),
            [0xFF, 0xFE, 0x00, 0x00, ..] => (Utf32LittleEndian, 4),
            [0xEF, 0xBB, 0xBF, ..] => (Utf8, 3),
            [0xFE, 0xFF, ..] => (Utf16BigEndian, 2),
            [0xFF, 0xFE, ..] => (Utf16LittleEndian, 2),
            _ => (Utf8, 0),
        };

        return encoding.GetString(content[preambleLength..]);
    }

    private WorkerLinkException Reject(string reason)
    {
        // Never attach configuration values or parser exceptions.
        Log.HostConfigurationRejected(logger, reason);

        return new(WorkerLinkFailureReason.Unavailable, reason);
    }

    private static partial class Log
    {
        [LoggerMessage(0, LogLevel.Warning, "Host configuration was rejected. {Reason}")]
        public static partial void HostConfigurationRejected(ILogger logger, string reason);
    }
}
