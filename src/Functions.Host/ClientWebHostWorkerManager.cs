// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.WebHost;

namespace Azure.Functions.Host;

/// <summary>
/// Represents WebHost worker operations when worker lifecycle is controlled externally.
/// </summary>
internal sealed class ClientWebHostWorkerManager : IWebHostWorkerManager
{
    public Task SpecializeAsync() => Task.CompletedTask;

    public Task WorkerWarmupAsync() => Task.CompletedTask;
}
