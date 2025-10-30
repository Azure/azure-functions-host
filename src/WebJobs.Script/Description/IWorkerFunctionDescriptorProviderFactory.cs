// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using Microsoft.Azure.WebJobs.Script.Extensibility;

namespace Microsoft.Azure.WebJobs.Script.Description;

/// <summary>
/// Hooks for creating FunctionDescriptorProviders for external workers
/// </summary>
public interface IWorkerFunctionDescriptorProviderFactory
{
    FunctionDescriptorProvider CreateMultiWorkerDescriptorProvider(ScriptHost host, ICollection<IScriptBindingProvider> bindingProviders);

    FunctionDescriptorProvider CreateWorkerDescriptorProvider(ScriptHost host, string workerRuntime, ICollection<IScriptBindingProvider> bindingProviders);
}