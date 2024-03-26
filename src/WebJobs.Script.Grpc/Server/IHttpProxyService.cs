// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Description;

namespace Microsoft.Azure.WebJobs.Script.Grpc
{
    public interface IHttpProxyService
    {
        Task EnsureSuccessfulForwardingAsync(ScriptInvocationContext context);

        Task ForwardAsync(ScriptInvocationContext context, Uri httpUri);
    }
}