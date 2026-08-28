// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Threading.Channels;
using Microsoft.Azure.WebJobs.Script.Grpc.Messages;

namespace Microsoft.Azure.WebJobs.Script.Grpc;

internal readonly record struct ServerDuplexChannelEndpoints(
    ChannelReader<StreamingMessage> HostToWorkerReader,
    ChannelWriter<StreamingMessage> WorkerToHostWriter);
