// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs.Script.Workers.Http;

namespace Microsoft.Azure.WebJobs.Script.Workers
{
    internal interface IHttpWorkerProcessFactory
    {
        IWorkerProcess Create(string workerId, string scriptRootPath, HttpWorkerOptions httpWorkerOptions);
    }
}
