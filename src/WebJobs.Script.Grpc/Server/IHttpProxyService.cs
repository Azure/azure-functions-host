// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Description;
using Yarp.ReverseProxy.Forwarder;

namespace Microsoft.Azure.WebJobs.Script.Grpc
{
    public interface IHttpProxyService
    {
        ValueTask<ForwarderError> ForwardAsync(ScriptInvocationContext context, Uri httpUri);
    }
}