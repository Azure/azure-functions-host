// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs.Script.Http;
using Microsoft.Extensions.Logging;
using Yarp.ReverseProxy.Forwarder;

namespace Azure.Functions.Rpc.Client;

/// <summary>
/// Owns the root-lifetime forwarding transport used only by linked WorkerProxy channels.
/// </summary>
internal sealed class WorkerProxyHttpTransport(IHttpForwarder forwarder, ILogger<DefaultHttpProxyService> logger)
    : DefaultHttpProxyService(forwarder, logger, RpcClientFactory.CreateHttpHandler());
