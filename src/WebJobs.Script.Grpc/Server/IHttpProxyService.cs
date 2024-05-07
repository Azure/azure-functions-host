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

        /// <summary>
        /// Initiates a request forward and updates the current context to track forwarding operation.
        /// </summary>
        /// <param name="context">The <see cref="ScriptInvocationContext"/> for the HTTP invocation.</param>
        /// <param name="httpUri">The target URI used for forwarding.</param>
        void StartForwarding(ScriptInvocationContext context, Uri httpUri);
    }
}