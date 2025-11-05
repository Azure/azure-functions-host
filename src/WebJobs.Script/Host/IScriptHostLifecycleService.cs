// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Description;

namespace Microsoft.Azure.WebJobs.Script.Host;

// TODO: (OOP - Refactor) - Review this
// Making this similar to IHostedLifecycleService in dotnet, but only using events we need right now
public interface IScriptHostLifecycleService
{
    Task InitializedAsync(IEnumerable<FunctionMetadata> functions, CancellationToken cancellationToken);

    Task StoppingAsync(CancellationToken cancellationToken);
}
