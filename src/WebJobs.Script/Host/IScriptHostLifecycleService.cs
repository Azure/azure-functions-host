// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Description;

namespace Microsoft.Azure.WebJobs.Script.Host;

/// <summary>
/// Defines methods that can be run at specific points in the ScriptHost lifecycle. Similar to
/// <see cref="Microsoft.Extensions.Hosting.IHostedLifecycleService"/>, but specific to the ScriptHost.
/// </summary>
public interface IScriptHostLifecycleService
{
    /// <summary>
    /// Triggered at the end of <see cref="ScriptHost.InitializeAsync(CancellationToken)"/>.
    /// </summary>
    /// <param name="cancellationToken">Indicates that the start process has been aborted.</param>
    /// <returns>A <see cref="Task"/> that represents the asynchronous operation.</returns>
    Task InitializedAsync(IEnumerable<FunctionMetadata> functions, CancellationToken cancellationToken);

    /// <summary>
    /// Triggered before <see cref="ScriptHost.StopAsyncCore(CancellationToken)"/>.
    /// </summary>
    /// <param name="cancellationToken">Indicates that the stop process has been aborted.</param>
    /// <returns>A <see cref="Task"/> that represents the asynchronous operation.</returns>
    Task StoppingAsync(CancellationToken cancellationToken);
}
