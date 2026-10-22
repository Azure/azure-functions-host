// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

#nullable enable

using System;
using System.Text;
using Microsoft.Azure.WebJobs.Script;
using Microsoft.Azure.WebJobs.Script.Configuration;
using Microsoft.Azure.WebJobs.Script.Grpc.Messages;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Azure.Functions.Rpc.Client;

/// <summary>
/// Validates one worker's delivery without publishing configuration or performing file I/O.
/// </summary>
internal sealed partial class WorkerHostJsonConfiguration(
    IOptions<RpcClientOptions> options,
    IConfiguration configuration,
    IOptionsMonitor<ScriptApplicationHostOptions> applicationOptions,
    ILogger<WorkerHostJsonConfiguration> logger)
{
    internal const string Capability = "HostJson";
    internal const string CapabilityVersion = "1";
    internal const int MaximumContentLength = 65_536;

    private static readonly Encoding Utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
    private static readonly Encoding Utf16LittleEndian = new UnicodeEncoding(bigEndian: false, byteOrderMark: false, throwOnInvalidBytes: true);
    private static readonly Encoding Utf16BigEndian = new UnicodeEncoding(bigEndian: true, byteOrderMark: false, throwOnInvalidBytes: true);
    private static readonly Encoding Utf32LittleEndian = new UTF32Encoding(bigEndian: false, byteOrderMark: false, throwOnInvalidCharacters: true);
    private static readonly Encoding Utf32BigEndian = new UTF32Encoding(bigEndian: true, byteOrderMark: false, throwOnInvalidCharacters: true);

    public bool Enabled { get; } = options.Value.EnableWorkerHostJson;

    public HostJsonConfigurationSnapshot Validate(HostJsonConfiguration? delivery)
    {
        if (delivery is null)
        {
            throw Reject("The worker did not deliver the requested host.json configuration.");
        }

        if (delivery.Content.Length > MaximumContentLength)
        {
            throw Reject("The worker host.json exceeds the 65,536-byte limit.");
        }

        if (delivery.Status != HostJsonConfiguration.Types.Status.Available && !delivery.Content.IsEmpty)
        {
            throw Reject("The worker host.json delivery has content inconsistent with its status.");
        }

        string? content = delivery.Status switch
        {
            HostJsonConfiguration.Types.Status.Available => Decode(delivery.Content.Span),
            HostJsonConfiguration.Types.Status.Missing => null,
            HostJsonConfiguration.Types.Status.ReadError => throw Reject("The worker could not read host.json."),
            HostJsonConfiguration.Types.Status.TooLarge => throw Reject("The worker host.json exceeds the 65,536-byte limit."),
            _ => throw Reject("The worker host.json delivery status is unspecified or unsupported."),
        };

        HostJsonFileConfigurationOptions hostJsonOptions = new(configuration, applicationOptions.CurrentValue);
        try
        {
            return HostJsonConfigurationSnapshot.Parse(content, hostJsonOptions);
        }
        catch (HostConfigurationException)
        {
            throw Reject("The worker host.json is not valid JSON with a supported schema version and configuration profile.");
        }
    }

    private string Decode(ReadOnlySpan<byte> content)
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

        try
        {
            return encoding.GetString(content[preambleLength..]);
        }
        catch (DecoderFallbackException)
        {
            throw Reject("The worker host.json contains invalid encoded text.");
        }
    }

    private WorkerLinkException Reject(string reason)
    {
        // Only fixed reasons reach this boundary; never attach worker values or parsing exceptions.
        Log.HostJsonConfigurationRejected(logger, reason);

        return new(WorkerLinkFailureReason.Unavailable, reason);
    }

    private static partial class Log
    {
        [LoggerMessage(0, LogLevel.Warning, "Worker host.json configuration was rejected. {Reason}")]
        public static partial void HostJsonConfigurationRejected(ILogger logger, string reason);
    }
}
