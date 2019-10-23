// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs.Script.OutOfProc.Http;
using Microsoft.Azure.WebJobs.Script.Rpc;

namespace Microsoft.Azure.WebJobs.Script.OutOfProc
{
    public interface IHttpWorkerProcessFactory
    {
        ILanguageWorkerProcess Create(string workerId, string scriptRootPath, HttpWorkerOptions httpWorkerOptions);
    }
}
