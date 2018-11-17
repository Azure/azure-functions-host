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
using System.Threading.Tasks.Dataflow;
using Microsoft.Azure.WebJobs.Script.Binding;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;

namespace Microsoft.Azure.WebJobs.Script.Description
{
    internal class WorkerLanguageInvoker : FunctionInvokerBase
    {
        private readonly Collection<FunctionBinding> _inputBindings;
        private readonly Collection<FunctionBinding> _outputBindings;
        private readonly BindingMetadata _trigger;
        private readonly ILogger _logger;
        private readonly Action<ScriptInvocationResult> _handleScriptReturnValue;
        private readonly BufferBlock<ScriptInvocationContext> _invocationBuffer;

        internal WorkerLanguageInvoker(ScriptHost host, BindingMetadata trigger, FunctionMetadata functionMetadata, ILoggerFactory loggerFactory,
            Collection<FunctionBinding> inputBindings, Collection<FunctionBinding> outputBindings, BufferBlock<ScriptInvocationContext> invocationBuffer)
            : base(host, functionMetadata, loggerFactory)
        {
            _trigger = trigger;
            _inputBindings = inputBindings;
            _outputBindings = outputBindings;
            _invocationBuffer = invocationBuffer;
            _logger = loggerFactory.CreateLogger("Host.WorkerLanguageInvoker");
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
            _logger.LogInformation("In InvokeCore");
            string invocationId = context.ExecutionContext.InvocationId.ToString();
            _logger.LogInformation($"In InvokeCore invocationId: {invocationId}");
            // TODO: fix extensions and remove
            object triggerValue = TransformInput(parameters[0], context.Binder.BindingData);
            var triggerInput = (_trigger.Name, _trigger.DataType ?? DataType.String, triggerValue);
            var inputs = new[] { triggerInput }.Concat(await BindInputsAsync(context.Binder));

            ScriptInvocationContext invocationContext = new ScriptInvocationContext()
            {
                FunctionMetadata = Metadata,
                BindingData = context.Binder.BindingData,
                ExecutionContext = context.ExecutionContext,
                Inputs = inputs,
                ResultSource = new TaskCompletionSource<ScriptInvocationResult>(),
                AsyncExecutionContext = System.Threading.ExecutionContext.Capture(),

                // TODO: link up cancellation token to parameter descriptors
                CancellationToken = CancellationToken.None,
                Logger = context.Logger
            };

            ScriptInvocationResult result;
            _logger.LogInformation($"In _invocationBuffer is null {_invocationBuffer == null}");
            _logger.LogInformation($"In InvokeCore posting to _invocationBuffer.count {_invocationBuffer.Count}");
            _logger.LogInformation($"In InvokeCore posting to _invocationBuffer");
            _invocationBuffer.Post(invocationContext);
            result = await invocationContext.ResultSource.Task;

            await BindOutputsAsync(triggerValue, context.Binder, result);
            return result.Return;
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
                var dataType = _trigger.DataType ?? DataType.String;
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
