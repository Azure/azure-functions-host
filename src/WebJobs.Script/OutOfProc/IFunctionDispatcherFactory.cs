// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs.Script.Rpc;

namespace Microsoft.Azure.WebJobs.Script.OutOfProc
{
    public interface IFunctionDispatcherFactory
    {
        IFunctionDispatcher GetFunctionDispatcher();
    }
}
