// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Listeners;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.Description
{
    internal class FunctionGroupListenerDecorator : IListenerDecorator
    {
        private static readonly NoOpListener _noOpListener = new();
        private readonly IFunctionMetadataManager _metadata;
        private readonly IEnvironment _environment; // TODO: replace options pattern
        private readonly ILogger _logger;

        public FunctionGroupListenerDecorator(
            IFunctionMetadataManager metadata,
            IEnvironment environment,
            ILogger<FunctionGroupListenerDecorator> logger)
        {
            _metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
            _environment = environment ?? throw new ArgumentNullException(nameof(environment));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public IListener Decorate(ListenerDecoratorContext context)
        {
            if (context is null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            if (!_environment.TryGetFunctionsTargetGroup(out string targetGroup))
            {
                // If no group configured, short-circuit.
                return context.Listener;
            }

            _logger.LogInformation("Function group target is {targetGroup}", targetGroup);
            string functionName = context.FunctionDefinition.Descriptor.ShortName;
            if (!_metadata.TryGetFunctionMetadata(functionName, out FunctionMetadata functionMetadata))
            {
                _logger.LogWarning("Unable to find function metadata for function {functionName}", functionName);
                return context.Listener;
            }

            string group = functionMetadata.GetFunctionGroup() ?? functionName;
            if (string.Equals(targetGroup, group, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogDebug("Enabling function {functionName}", functionName);
                return context.Listener;
            }

            // A target function group is configured and this function is not part of it.
            // By giving a no-op listener, we will prevent it from triggering without 'disabling' it.
            _logger.LogDebug("Function {functionName} is not part of group {functionGroup}. Listener will not be enabled.", functionName, targetGroup);
            context.Listener?.Dispose(); // this will not be used, lets dispose it now.
            return _noOpListener;
        }

        private class NoOpListener : IListener
        {
            public void Cancel()
            {
            }

            public void Dispose()
            {
            }

            public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

            public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        }
    }
}
