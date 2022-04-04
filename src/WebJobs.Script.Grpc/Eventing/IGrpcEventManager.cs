// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

//using System.Threading.Channels;
//using Microsoft.Azure.WebJobs.Script.Eventing;

//namespace Microsoft.Azure.WebJobs.Script.Grpc.Eventing;

///// <summary>A script event manager with dedicated support for gRPC channels</summary>
//public interface IGrpcEventManager : IScriptEventManager
//{
//    /// <summary>Prepares the specified workerId for usage (unexpected inbound worker ids will be rejected)</summary>
//    void AddWorker(string workerId);

//    /// <summary>Obtains the gRPC channels for the specified workerId, if it has been prepared</summary>
//    bool TryGetGrpcChannels(string workerId, out Channel<InboundGrpcEvent> inbound, out Channel<OutboundGrpcEvent> outbound);
//}
