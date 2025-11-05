// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Diagnostics;
using System.Threading.Tasks;

namespace Microsoft.Azure.WebJobs.Script.Workers
{
    internal interface IWorkerProcess
    {
        int Id { get; }

        Process Process { get; }

        Task StartProcessAsync();

        void WaitForProcessExitInMilliSeconds(int waitTime);
    }
}