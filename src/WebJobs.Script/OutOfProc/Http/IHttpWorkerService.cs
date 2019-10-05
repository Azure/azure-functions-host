// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Description;

namespace Microsoft.Azure.WebJobs.Script.OutOfProc
{
    public interface IHttpWorkerService
    {
        Task InvokeAsync(ScriptInvocationContext scriptInvocationContext);
    }
}
