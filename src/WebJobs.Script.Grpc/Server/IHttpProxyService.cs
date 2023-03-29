﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Description;
using Yarp.ReverseProxy.Forwarder;

namespace Microsoft.Azure.WebJobs.Script.Grpc
{
    public interface IHttpProxyService
    {
        public ValueTask<ForwarderError> Forward(ScriptInvocationContext context, string httpProxyEndpoint);
    }
}