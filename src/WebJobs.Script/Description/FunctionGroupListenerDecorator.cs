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
        private readonly IFunctionMetadataManager _metadataManager;
        private readonly IEnvironment _environment; // TODO: replace options pattern
        private readonly ILogger _logger;

        public FunctionGroupListenerDecorator(
            IFunctionMetadataManager metadataManager,
            IEnvironment environment,
            ILogger<FunctionGroupListenerDecorator> logger)
        {
            _metadataManager = metadataManager ?? throw new ArgumentNullException(nameof(metadataManager));
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

            // The log name matches the internal metadata we track.
            string functionName = context.FunctionDefinition.Descriptor.LogName;
            if (!_metadataManager.TryGetFunctionMetadata(functionName, out FunctionMetadata functionMetadata))
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
            return new NoOpListener(context.Listener);
        }

        private class NoOpListener : IListener
        {
            private readonly IListener _listener;

            public NoOpListener(IListener listener)
            {
                // Only hold onto this listener for disposal.
                _listener = listener;
            }

            public void Cancel()
            {
            }

            public void Dispose()
            {
                _listener.Dispose();
            }

            public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

            public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        }
    }
}
