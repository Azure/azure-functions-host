// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs.Script.Workers.Http;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;

namespace Microsoft.Azure.WebJobs.Script.Workers
{
    public interface IHttpWorkerProcessFactory
    {
        IWorkerProcess Create(string workerId, string scriptRootPath, HttpWorkerOptions httpWorkerOptions);
    }
}
