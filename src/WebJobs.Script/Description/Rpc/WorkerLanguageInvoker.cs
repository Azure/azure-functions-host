// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Binding;
using Microsoft.Azure.WebJobs.Script.Rpc;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;

namespace Microsoft.Azure.WebJobs.Script.Description
{
    internal class WorkerLanguageInvoker : FunctionInvokerBase
    {
        private readonly Collection<FunctionBinding> _inputBindings;
        private readonly Collection<FunctionBinding> _outputBindings;
        private readonly BindingMetadata _bindingMetadata;
        private readonly ILogger _logger;
        private readonly Action<ScriptInvocationResult> _handleScriptReturnValue;
        private readonly IFunctionDispatcher _functionDispatcher;

        internal WorkerLanguageInvoker(ScriptHost host, BindingMetadata bindingMetadata, FunctionMetadata functionMetadata, ILoggerFactory loggerFactory,
            Collection<FunctionBinding> inputBindings, Collection<FunctionBinding> outputBindings, IFunctionDispatcher fuctionDispatcher)
            : base(host, functionMetadata, loggerFactory)
        {
            _bindingMetadata = bindingMetadata;
            _inputBindings = inputBindings;
            _outputBindings = outputBindings;
            _functionDispatcher = fuctionDispatcher;
            _logger = loggerFactory.CreateLogger<WorkerLanguageInvoker>();

            InitializeFileWatcherIfEnabled();

            if (_outputBindings.Any(p => p.Metadata.IsReturn))
            {
                _handleScriptReturnValue = HandleReturnParameter;
            }
            else
            {
                _handleScriptReturnValue = HandleOutputDictionary;
            }
        }

        protected override async Task<object> InvokeCore(object[] parameters, FunctionInvocationContext context)
        {
            // Need to wait for atleast one language worker process to be initialized before accepting invocations
            await DelayUntilFunctionDispatcherInitialized();
            string invocationId = context.ExecutionContext.InvocationId.ToString();

            // TODO: fix extensions and remove
            object triggerValue = TransformInput(parameters[0], context.Binder.BindingData);
            var triggerInput = (_bindingMetadata.Name, _bindingMetadata.DataType ?? DataType.String, triggerValue);
            var inputs = new[] { triggerInput }.Concat(await BindInputsAsync(context.Binder));

            ScriptInvocationContext invocationContext = new ScriptInvocationContext()
            {
                FunctionMetadata = Metadata,
                BindingData = context.Binder.BindingData,   // This has duplicates too (of type DefaultHttpRequest). Needs to be removed after verifying this can indeed be constructed by the workers from the rest of the data being passed (https://github.com/Azure/azure-functions-host/issues/4735).
                ExecutionContext = context.ExecutionContext,
                Inputs = inputs,
                ResultSource = new TaskCompletionSource<ScriptInvocationResult>(),
                AsyncExecutionContext = System.Threading.ExecutionContext.Capture(),

                // TODO: link up cancellation token to parameter descriptors
                CancellationToken = CancellationToken.None,
                Logger = context.Logger
            };

            ScriptInvocationResult result;
            _logger.LogDebug($"Sending invocation id:{invocationId}");
            _functionDispatcher.Invoke(invocationContext);
            result = await invocationContext.ResultSource.Task;

            await BindOutputsAsync(triggerValue, context.Binder, result);
            return result.Return;
        }

        private async Task DelayUntilFunctionDispatcherInitialized()
        {
            if (_functionDispatcher != null && _functionDispatcher.State == FunctionDispatcherState.Initializing)
            {
                _logger.LogDebug($"functionDispatcher state: {_functionDispatcher.State}");
                await Utility.DelayAsync(LanguageWorkerConstants.ProcessStartTimeoutSeconds, 25, () =>
                {
                    return _functionDispatcher.State != FunctionDispatcherState.Initialized;
                });
            }
        }

        private async Task<(string name, DataType type, object value)[]> BindInputsAsync(Binder binder)
        {
            var bindingTasks = _inputBindings
                .Where(binding => !binding.Metadata.IsTrigger)
                .Select(async (binding) =>
                {
                    BindingContext bindingContext = new BindingContext
                    {
                        Binder = binder,
                        BindingData = binder.BindingData,
                        DataType = binding.Metadata.DataType ?? DataType.String,
                        Cardinality = binding.Metadata.Cardinality ?? Cardinality.One
                    };

                    await binding.BindAsync(bindingContext).ConfigureAwait(false);
                    return (binding.Metadata.Name, bindingContext.DataType, bindingContext.Value);
                });

            return await Task.WhenAll(bindingTasks);
        }

        private async Task BindOutputsAsync(object input, Binder binder, ScriptInvocationResult result)
        {
            if (_outputBindings == null)
            {
                return;
            }

            _handleScriptReturnValue(result);

            var outputBindingTasks = _outputBindings.Select(async binding =>
            {
                // apply the value to the binding
                if (result.Outputs.TryGetValue(binding.Metadata.Name, out object value) && value != null)
                {
                    BindingContext bindingContext = new BindingContext
                    {
                        TriggerValue = input,
                        Binder = binder,
                        BindingData = binder.BindingData,
                        Value = value
                    };
                    await binding.BindAsync(bindingContext).ConfigureAwait(false);
                }
            });

            await Task.WhenAll(outputBindingTasks);
        }

        private object TransformInput(object input, Dictionary<string, object> bindingData)
        {
            if (input is Stream)
            {
                var dataType = _bindingMetadata.DataType ?? DataType.String;
                FunctionBinding.ConvertStreamToValue((Stream)input, dataType, ref input);
            }

            // TODO: investigate moving POCO style binding addition to sdk
            Utility.ApplyBindingData(input, bindingData);
            return input;
        }

        private void HandleReturnParameter(ScriptInvocationResult result)
        {
            result.Outputs[ScriptConstants.SystemReturnParameterBindingName] = result.Return;
        }

        private void HandleOutputDictionary(ScriptInvocationResult result)
        {
            if (result.Return is JObject returnJson)
            {
                foreach (var pair in returnJson)
                {
                    result.Outputs[pair.Key] = pair.Value.ToObject<object>();
                }
            }
        }
    }
}