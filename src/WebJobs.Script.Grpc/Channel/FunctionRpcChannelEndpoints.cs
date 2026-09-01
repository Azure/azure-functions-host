// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Threading.Channels;
using Microsoft.Azure.WebJobs.Script.Grpc.Messages;

namespace Microsoft.Azure.WebJobs.Script.Grpc;

/// <summary>
/// Provides the borrowed channel endpoints used by <c>FunctionRpcService</c> to bridge a worker's gRPC stream.
/// </summary>
/// <param name="HostToWorkerReader">Reads host messages that should be written to the worker.</param>
/// <param name="WorkerToHostWriter">Writes messages received from the worker for host processing.</param>
/// <remarks>
/// This value does not own the channel lifetime. The associated <see cref="ServerDuplexChannel"/> owns both endpoints.
/// </remarks>
internal readonly record struct FunctionRpcChannelEndpoints(
    ChannelReader<StreamingMessage> HostToWorkerReader,
    ChannelWriter<StreamingMessage> WorkerToHostWriter);
