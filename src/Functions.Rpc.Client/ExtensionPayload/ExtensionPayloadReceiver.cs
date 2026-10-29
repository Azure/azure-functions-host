// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Grpc.Messages;

namespace Azure.Functions.Rpc.Client;

/// <summary>
/// Receives one ordered payload without taking ownership of the caller's staging stream.
/// </summary>
internal sealed class ExtensionPayloadReceiver(Stream destination, CancellationToken cancellationToken)
{
    private readonly Lock _stateLock = new();
    private readonly Stream _destination = destination ?? throw new ArgumentNullException(nameof(destination));
    private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Task _pendingWrite = Task.CompletedTask;
    private ulong _received;
    private bool _stopped;

    internal string RequestId { get; } = Guid.NewGuid().ToString();

    internal Task Completion => _completion.Task;

    internal ulong ReceivedBytes => _received;

    internal Task ReceiveAsync(ExtensionPayloadResponse response)
    {
        lock (_stateLock)
        {
            if (_stopped)
            {
                return Task.CompletedTask;
            }

            // The channel awaits each receive in order. Retain that write so cancellation cannot close its stream early.
            return _pendingWrite = ReceiveCoreAsync(response);
        }
    }

    private async Task ReceiveCoreAsync(ExtensionPayloadResponse response)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_completion.Task.IsCompleted || response.Offset != _received)
            {
                throw new InvalidDataException("The extension payload contained an unexpected message offset or duplicate terminal result.");
            }

            if (response.ContentCase is ExtensionPayloadResponse.ContentOneofCase.Chunk)
            {
                int count = response.Chunk.Length;
                if (count is 0 or > WorkerExtensionPayloadManager.MaxChunkBytes ||
                    (ulong)count > (ulong)WorkerExtensionPayloadManager.MaxCompressedBytes - _received)
                {
                    throw new InvalidDataException("The extension payload exceeded its chunk or compressed size limit.");
                }

                await _destination.WriteAsync(response.Chunk.Memory, cancellationToken);
                _received += (ulong)count;
            }
            else if (response.ContentCase is ExtensionPayloadResponse.ContentOneofCase.Result &&
                     response.Result.Status is StatusResult.Types.Status.Success && _received > 0)
            {
                _completion.TrySetResult();
            }
            else
            {
                throw new InvalidDataException("The worker did not complete the extension payload successfully.");
            }
        }
        catch (Exception exception)
        {
            _completion.TrySetException(exception);
        }
    }

    internal async Task StopAsync()
    {
        Task pendingWrite;
        lock (_stateLock)
        {
            _stopped = true;
            pendingWrite = _pendingWrite;
        }

        await pendingWrite;
    }
}
