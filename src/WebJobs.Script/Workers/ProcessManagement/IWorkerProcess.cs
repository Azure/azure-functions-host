// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Scale;

namespace Microsoft.Azure.WebJobs.Script.Workers
{
    public interface IWorkerProcess
    {
        int Id { get; }

        Task StartProcessAsync();

        ProcessStats GetStats();
    }
}