// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.WebJobs.Script.Composition;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Composition;

/// <summary>
/// Preserves the composition that configured the root WebHost for use by later child ScriptHost builds.
/// </summary>
/// <remarks>
/// The public <see cref="IWorkerComposition"/> interface is not used as the DI selection key because unrelated
/// registrations could replace it after the root services were already configured. This internal holder keeps root
/// and child composition aligned across ScriptHost restarts.
/// </remarks>
internal sealed class SelectedWorkerComposition(IWorkerComposition composition)
{
    public IWorkerComposition Value { get; } = composition ?? throw new ArgumentNullException(nameof(composition));
}
