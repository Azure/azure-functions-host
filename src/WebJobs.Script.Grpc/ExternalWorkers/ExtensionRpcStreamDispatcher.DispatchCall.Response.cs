// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

#nullable enable

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Google.Protobuf;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Azure.WebJobs.Script.Grpc.Messages;
using Microsoft.Extensions.Primitives;

namespace Microsoft.Azure.WebJobs.Script.Grpc.ExternalWorkers;

internal sealed partial class ExtensionRpcStreamDispatcher
{
    private static readonly HashSet<string> ExcludedResponseHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        // The proxy establishes the worker-facing response content type for the reconstructed gRPC call.
        "content-type",

        // The terminal lifecycle message carries gRPC status and detail so they can be emitted exactly once.
        "grpc-message",
        "grpc-status",
    };

    private sealed partial class DispatchCall
    {
        private async Task RelayResponseAsync(
            HttpResponse response,
            ResponseTrailersFeature trailersFeature,
            System.IO.Pipelines.PipeReader responseReader,
            Task endpointTask,
            CancellationToken cancellationToken)
        {
            using Stream responseStream = responseReader.AsStream();
            byte[] prefix = new byte[5];
            ulong messageId = 0;
            bool headersSent = false;

            while (await TryReadExactlyAsync(responseStream, prefix, cancellationToken))
            {
                if (!headersSent)
                {
                    await SendHeadersAsync(response.Headers, cancellationToken);
                    headersSent = true;
                }

                uint messageLength = BinaryPrimitives.ReadUInt32BigEndian(prefix.AsSpan(1));
                if (messageLength > _maxMessageSize)
                {
                    throw new InvalidDataException(
                        $"The extension response message length '{messageLength}' exceeds the negotiated limit "
                        + $"'{_maxMessageSize}'.");
                }

                bool compressed = prefix[0] is 1;
                if (prefix[0] is not 0 and not 1)
                {
                    throw new InvalidDataException("The extension endpoint wrote an invalid gRPC compression flag.");
                }

                messageId++;
                byte[] buffer = new byte[Math.Min(messageLength, _maxDataChunkSize)];
                ulong offset = 0;
                if (messageLength is 0)
                {
                    await SendDataAsync(
                        messageId,
                        0,
                        messageLength,
                        compressed,
                        ByteString.Empty,
                        true,
                        cancellationToken);
                }

                while (offset < messageLength)
                {
                    int count = (int)Math.Min((ulong)buffer.Length, messageLength - offset);
                    await ReadExactlyAsync(responseStream, buffer.AsMemory(0, count), cancellationToken);
                    await SendDataAsync(
                        messageId,
                        offset,
                        messageLength,
                        compressed,
                        ByteString.CopyFrom(buffer, 0, count),
                        offset + (uint)count == messageLength,
                        cancellationToken);
                    offset += (uint)count;
                }
            }

            await endpointTask;
            if (!headersSent)
            {
                await SendHeadersAsync(response.Headers, cancellationToken);
            }

            ExtensionRpcStatus status = ParseStatus(trailersFeature.Trailers["grpc-status"]);
            string detail = Uri.UnescapeDataString(trailersFeature.Trailers["grpc-message"].ToString());
            await CompleteAsync(
                status,
                detail,
                ToMetadata(trailersFeature.Trailers),
                cancellationToken);
        }

        private async ValueTask SendHeadersAsync(IHeaderDictionary headers, CancellationToken cancellationToken)
        {
            var message = new ExtensionRpcMessage
            {
                SessionId = _sessionId,
                ShardId = _shardId,
                CallId = _callId,
                Headers = new ExtensionRpcHeaders(),
            };
            message.Headers.Metadata.AddRange(ToMetadata(headers));
            await WriteAsync(message, cancellationToken);
        }

        private async ValueTask SendDataAsync(
            ulong messageId,
            ulong offset,
            ulong messageLength,
            bool compressed,
            ByteString payload,
            bool endOfMessage,
            CancellationToken cancellationToken)
        {
            await _responseCredits.ReserveAsync((ulong)payload.Length, cancellationToken);
            await WriteAsync(
                new ExtensionRpcMessage
                {
                    SessionId = _sessionId,
                    ShardId = _shardId,
                    CallId = _callId,
                    Data = new ExtensionRpcData
                    {
                        MessageId = messageId,
                        Offset = offset,
                        MessageLength = messageLength,
                        Compressed = compressed,
                        Payload = payload,
                        EndOfMessage = endOfMessage,
                    },
                },
                cancellationToken);
        }

        private async ValueTask CompleteAsync(
            ExtensionRpcStatus status,
            string detail,
            ExtensionRpcMetadataEntry[] trailers,
            CancellationToken cancellationToken)
        {
            if (Interlocked.Exchange(ref _completed, 1) != 0)
            {
                return;
            }

            var message = new ExtensionRpcMessage
            {
                SessionId = _sessionId,
                ShardId = _shardId,
                CallId = _callId,
                Complete = new ExtensionRpcComplete
                {
                    Status = status,
                    Detail = detail,
                },
            };
            message.Complete.Trailers.AddRange(trailers);
            try
            {
                await WriteAsync(message, cancellationToken);
            }
            catch
            {
                // A cancelled or closed write did not publish the terminal message. Allow the failure path to retry
                // with a bounded cleanup token so the proxy is not left waiting for a call that was removed locally.
                Interlocked.CompareExchange(ref _completed, 0, 1);
                throw;
            }
        }

        private async ValueTask TryCompleteAfterFailureAsync(
            ExtensionRpcStatus status,
            string detail,
            ExtensionRpcMetadataEntry[] trailers)
        {
            using CancellationTokenSource cleanupCancellationTokenSource =
                CancellationTokenSource.CreateLinkedTokenSource(_sessionCancellationToken);
            cleanupCancellationTokenSource.CancelAfter(TerminalWriteTimeout);
            try
            {
                await CompleteAsync(status, detail, trailers, cleanupCancellationTokenSource.Token);
            }
            catch (ChannelClosedException)
            {
                Log.StreamClosedBeforeTerminalStatus(_logger, _callId, status);
            }
            catch (OperationCanceledException) when (cleanupCancellationTokenSource.IsCancellationRequested)
            {
                Log.TerminalStatusDropped(_logger, status, _callId);
            }
        }

        private async ValueTask WriteAsync(ExtensionRpcMessage message, CancellationToken cancellationToken)
        {
            await _outbound.WriteAsync(message, cancellationToken);
        }

        private static void AddRequestMetadata(
            IHeaderDictionary headers,
            IEnumerable<ExtensionRpcMetadataEntry> metadata)
        {
            foreach (ExtensionRpcMetadataEntry entry in metadata)
            {
                string value = entry.Key.EndsWith("-bin", StringComparison.OrdinalIgnoreCase)
                    ? Convert.ToBase64String(entry.Value.Span)
                    : entry.Value.ToStringUtf8();
                headers.Append(entry.Key, value);
            }
        }

        private static ExtensionRpcMetadataEntry[] ToMetadata(IHeaderDictionary headers)
        {
            var metadata = new List<ExtensionRpcMetadataEntry>();
            foreach ((string key, StringValues values) in headers)
            {
                if (ExcludedResponseHeaders.Contains(key))
                {
                    continue;
                }

                foreach (string? value in values)
                {
                    if (value is null)
                    {
                        continue;
                    }

                    string[] metadataValues = key.EndsWith("-bin", StringComparison.OrdinalIgnoreCase)
                        ? value.Split(',', StringSplitOptions.TrimEntries)
                        : [value];
                    foreach (string metadataValue in metadataValues)
                    {
                        metadata.Add(
                            new ExtensionRpcMetadataEntry
                            {
                                Key = key,
                                Value = key.EndsWith("-bin", StringComparison.OrdinalIgnoreCase)
                                    ? ByteString.CopyFrom(DecodeBinaryMetadata(metadataValue))
                                    : ByteString.CopyFrom(metadataValue, Encoding.UTF8),
                            });
                    }
                }
            }

            return metadata.ToArray();
        }

        private static byte[] DecodeBinaryMetadata(string value)
        {
            int remainder = value.Length % 4;
            if (remainder is 1)
            {
                throw new FormatException("Binary gRPC metadata contains invalid base64.");
            }

            return Convert.FromBase64String(remainder is 0 ? value : value.PadRight(value.Length + 4 - remainder, '='));
        }

        private static ExtensionRpcStatus ParseStatus(StringValues value)
        {
            return int.TryParse(value.ToString(), NumberStyles.None, CultureInfo.InvariantCulture, out int status)
                && Enum.IsDefined(typeof(ExtensionRpcStatus), status)
                    ? (ExtensionRpcStatus)status
                    : ExtensionRpcStatus.Unknown;
        }

        private static async Task<bool> TryReadExactlyAsync(
            Stream stream,
            Memory<byte> buffer,
            CancellationToken cancellationToken)
        {
            int offset = 0;
            while (offset < buffer.Length)
            {
                int count = await stream.ReadAsync(buffer[offset..], cancellationToken);
                if (count is 0)
                {
                    if (offset is 0)
                    {
                        return false;
                    }

                    throw new InvalidDataException("The gRPC response frame ended unexpectedly.");
                }

                offset += count;
            }

            return true;
        }

        private static async Task ReadExactlyAsync(
            Stream stream,
            Memory<byte> buffer,
            CancellationToken cancellationToken)
        {
            if (!await TryReadExactlyAsync(stream, buffer, cancellationToken))
            {
                throw new InvalidDataException("The gRPC response payload ended unexpectedly.");
            }
        }
    }

    private sealed class ResponseTrailersFeature : IHttpResponseTrailersFeature
    {
        /// <inheritdoc/>
        public IHeaderDictionary Trailers { get; set; } = new HeaderDictionary();
    }
}
