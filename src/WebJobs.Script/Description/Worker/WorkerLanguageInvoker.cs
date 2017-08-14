// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Dynamic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Script.Binding;
using Microsoft.Azure.WebJobs.Script.Description.Script;
using Microsoft.Azure.WebJobs.Script.Eventing;
using Microsoft.Azure.WebJobs.Script.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using MsgType = Microsoft.Azure.WebJobs.Script.Grpc.Messages.StreamingMessage.ContentOneofCase;

namespace Microsoft.Azure.WebJobs.Script.Description
{
    // TODO: make this internal
    public class WorkerLanguageInvoker : FunctionInvokerBase
    {
        private readonly Collection<FunctionBinding> _inputBindings;
        private readonly Collection<FunctionBinding> _outputBindings;
        private readonly BindingMetadata _trigger;
        private readonly Action<ScriptInvocationResult> _handleScriptReturnValue;

        internal WorkerLanguageInvoker(ScriptHost host, BindingMetadata trigger, FunctionMetadata functionMetadata,
            Collection<FunctionBinding> inputBindings, Collection<FunctionBinding> outputBindings)
            : base(host, functionMetadata)
        {
            _trigger = trigger;
            _inputBindings = inputBindings;
            _outputBindings = outputBindings;

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

        protected override async Task InvokeCore(object[] parameters, FunctionInvocationContext context)
        {
            var logHandler = CreateLogHandler(context.Logger);
            string invocationId = context.ExecutionContext.InvocationId.ToString();
            var logSubscription = Host.EventManager
                .OfType<RpcEvent>()
                .Where(evt => evt.MessageType == MsgType.RpcLog && evt.Message.RpcLog.InvocationId == invocationId)
                .Subscribe(logHandler);

            // TODO: fix extensions and remove
            object triggerValue = TransformInput(parameters[0], context.Binder.BindingData);
            var triggerInput = (_trigger.Name, _trigger.DataType ?? DataType.String, triggerValue);
            var inputs = new[] { triggerInput }.Concat(await BindInputsAsync(context.Binder));

            ScriptInvocationContext invocationContext = new ScriptInvocationContext()
            {
                BindingData = context.Binder.BindingData,
                ExecutionContext = context.ExecutionContext,
                Inputs = inputs
            };

            ScriptInvocationResult result;
            using (logSubscription)
            {
                result = await Host.FunctionDispatcher.InvokeAsync(Metadata, invocationContext);
            }

            await BindOutputsAsync(triggerValue, context.Binder, result);
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

        private static Action<RpcEvent> CreateLogHandler(ILogger logger)
        {
            return (rpcEvent) =>
            {
                var logMessage = rpcEvent.Message.RpcLog;
                if (logMessage.Message != null)
                {
                    LogLevel logLevel = (LogLevel)logMessage.Level;
                    logger.Log(logLevel, new EventId(0, logMessage.EventId), logMessage.Message, null, null);
                }
            };
        }
    }
}
