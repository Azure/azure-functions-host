// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Threading.Tasks;

namespace Microsoft.Azure.WebJobs.Host.Executors
{
    internal interface IMethodInvoker<TReflected>
    {
        // The cancellation token, if any, is provided along with the other arguments.
        Task InvokeAsync(TReflected instance, object[] arguments);
    }
}
