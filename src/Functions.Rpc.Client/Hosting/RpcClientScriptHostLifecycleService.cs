// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Logging;
using Microsoft.Azure.WebJobs.Script;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Host;
using Microsoft.Azure.WebJobs.Script.ManagedDependencies;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Azure.Functions.Rpc.Client;

/// <summary>
/// Activates root-owned client worker channels for one ScriptHost and initializes its local dispatcher lifecycle.
/// </summary>
internal sealed partial class RpcClientScriptHostLifecycleService(IWorkerChannelRegistry channelRegistry, IRpcClientFunctionInvocationDispatcher dispatcher,
    IOptions<ScriptJobHostOptions> scriptHostOptions, IOptions<ManagedDependencyOptions> managedDependencyOptions,
    ILogger<RpcClientScriptHostLifecycleService> logger) : IScriptHostLifecycleService
{
    internal const string FunctionGroupKey = "FunctionGroup";

    private readonly ILogger<RpcClientScriptHostLifecycleService> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    private readonly IWorkerChannelRegistry _channelRegistry =
        channelRegistry ?? throw new ArgumentNullException(nameof(channelRegistry));

    private readonly IRpcClientFunctionInvocationDispatcher _dispatcher =
        dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));

    private readonly ManagedDependencyOptions _managedDependencyOptions =
        managedDependencyOptions?.Value ?? throw new ArgumentNullException(nameof(managedDependencyOptions));

    private readonly ScriptJobHostOptions _scriptHostOptions =
        scriptHostOptions?.Value ?? throw new ArgumentNullException(nameof(scriptHostOptions));

    public async Task InitializedAsync(
        IEnumerable<FunctionMetadata> functions,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        FunctionMetadata[] functionArray = functions?.ToArray() ?? [];

        // Workers can emit function logs as soon as load requests are sent, so enrich metadata before activation.
        foreach (FunctionMetadata metadata in functionArray)
        {
            string functionGroup = metadata.GetFunctionGroup();
            if (metadata.Properties.TryGetValue(FunctionGroupKey, out object existing)
                && (existing is not string existingGroup || !string.Equals(existingGroup, functionGroup, StringComparison.OrdinalIgnoreCase)))
            {
                Log.FunctionGroupOverwritten(_logger, metadata.Name, functionGroup);
            }

            metadata.Properties[FunctionGroupKey] = functionGroup;
            metadata.Properties[LogConstants.CategoryNameKey] = LogCategories.CreateFunctionUserCategory(metadata.Name);
            metadata.Properties[ScriptConstants.LogPropertyHostInstanceIdKey] = _scriptHostOptions.InstanceId;
        }

        // Even an empty function set replaces the previous ScriptHost's channel setup context.
        await _channelRegistry.ActivateAsync(functionArray, _managedDependencyOptions, _scriptHostOptions.FunctionTimeout, cancellationToken);

        // Channel setup belongs to the root registry; only this child's invocation lifecycle remains to initialize.
        await _dispatcher.InitializeAsync(functionArray, cancellationToken);
    }

    public Task StoppingAsync(CancellationToken cancellationToken)
    {
        // Stop this child's dispatch without disposing the root-owned channels reused by a replacement ScriptHost.
        _dispatcher.PreShutdown();
        return Task.CompletedTask;
    }

    private static partial class Log
    {
        [LoggerMessage(0, LogLevel.Warning, "Replacing conflicting FunctionGroup metadata property for function {FunctionName} with the Host-derived group {FunctionGroup}.")]
        public static partial void FunctionGroupOverwritten(ILogger logger, string functionName, string functionGroup);
    }
}
