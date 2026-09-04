// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

#nullable enable

using System;
using System.Buffers.Binary;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Grpc.Messages;

namespace Microsoft.Azure.WebJobs.Script.Grpc.ExternalWorkers;

internal sealed partial class ExtensionRpcStreamDispatcher
{
    private sealed partial class DispatchCall
    {
        private async Task RelayRequestAsync(
            System.IO.Pipelines.PipeWriter requestWriter,
            CancellationToken cancellationToken)
        {
            ulong? messageId = null;
            ulong messageLength = 0;
            ulong nextOffset = 0;
            Exception? error = null;
            try
            {
                await foreach (ExtensionRpcMessage message in _inbound.Reader.ReadAllAsync(cancellationToken))
                {
                    switch (message.ContentCase)
                    {
                        case ExtensionRpcMessage.ContentOneofCase.Data:
                            ExtensionRpcData data = message.Data;
                            if (data.MessageLength > _maxMessageSize
                                || data.Payload.Length > _maxDataChunkSize)
                            {
                                throw new InvalidDataException("The extension request exceeded negotiated transport limits.");
                            }

                            bool startsMessage = data.Offset is 0;
                            if (startsMessage)
                            {
                                if (messageId is not null || data.MessageLength > uint.MaxValue)
                                {
                                    throw new InvalidDataException("Invalid extension request message framing.");
                                }

                                messageId = data.MessageId;
                                messageLength = data.MessageLength;
                                nextOffset = 0;
                            }

                            if (messageId != data.MessageId || data.Offset != nextOffset)
                            {
                                throw new InvalidDataException("Extension request chunks are out of order.");
                            }

                            ulong remainingLength = messageLength - nextOffset;
                            if ((ulong)data.Payload.Length > remainingLength
                                || (data.EndOfMessage && (ulong)data.Payload.Length != remainingLength)
                                || (!data.EndOfMessage && (ulong)data.Payload.Length >= remainingLength))
                            {
                                throw new InvalidDataException(
                                    "Extension request chunk exceeds its declared message length.");
                            }

                            if (startsMessage)
                            {
                                Span<byte> prefix = requestWriter.GetSpan(5);
                                prefix[0] = data.Compressed ? (byte)1 : (byte)0;
                                BinaryPrimitives.WriteUInt32BigEndian(prefix[1..], (uint)data.MessageLength);
                                requestWriter.Advance(5);
                            }

                            data.Payload.Span.CopyTo(requestWriter.GetSpan(data.Payload.Length));
                            requestWriter.Advance(data.Payload.Length);
                            nextOffset += (ulong)data.Payload.Length;
                            await requestWriter.FlushAsync(cancellationToken);
                            await WriteAsync(
                                new ExtensionRpcMessage
                                {
                                    SessionId = _sessionId,
                                    ShardId = _shardId,
                                    CallId = _callId,
                                    WindowUpdate = new ExtensionRpcWindowUpdate
                                    {
                                        ByteCount = (ulong)data.Payload.Length,
                                    },
                                },
                                cancellationToken);

                            if (data.EndOfMessage)
                            {
                                if (nextOffset != messageLength)
                                {
                                    throw new InvalidDataException("Extension request message length did not match its chunks.");
                                }

                                messageId = null;
                            }

                            break;
                        case ExtensionRpcMessage.ContentOneofCase.HalfClose:
                            return;
                        case ExtensionRpcMessage.ContentOneofCase.Cancel:
                            Cancel();
                            return;
                    }
                }
            }
            catch (Exception exception)
            {
                error = exception;
                Cancel();
                throw;
            }
            finally
            {
                await requestWriter.CompleteAsync(error);
            }
        }
    }
}
