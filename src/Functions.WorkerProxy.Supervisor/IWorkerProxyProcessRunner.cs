// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace Microsoft.Azure.Functions.WorkerProxy.Supervisor;

internal interface IWorkerProxyProcessRunner
{
    Task<int> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        Action<string> onOutputLine,
        CancellationToken cancellationToken);
}
