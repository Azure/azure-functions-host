// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Reactive.Linq;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Script.Binding;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using OutOfProcModel.Abstractions.Worker;

namespace Microsoft.Azure.WebJobs.Script.Description
{
    // TODO: has to be public for reflection stuff? Revisit.
    public class WorkerModelFunctionInvoker : IFunctionInvoker
    {
        private readonly Collection<FunctionBinding> _inputBindings;
        private readonly Collection<FunctionBinding> _outputBindings;
        private readonly BindingMetadata _bindingMetadata;
        private readonly FunctionMetadata _metadata;
        private readonly ILogger _logger;
        private readonly Action<ScriptInvocationResult> _handleScriptReturnValue;
        private readonly IWorkerResolver _workerResolver;
        // private readonly IApplicationLifetime _applicationLifetime;
        // private readonly TimeSpan _workerInitializationTimeout;

        internal WorkerModelFunctionInvoker(BindingMetadata bindingMetadata, FunctionMetadata functionMetadata, ILoggerFactory loggerFactory,
            Collection<FunctionBinding> inputBindings, Collection<FunctionBinding> outputBindings, IWorkerResolver workerResolver)
        {
            _bindingMetadata = bindingMetadata;
            _metadata = functionMetadata;
            _inputBindings = inputBindings;
            _outputBindings = outputBindings;
            _workerResolver = workerResolver;
            _logger = loggerFactory.CreateLogger<WorkerModelFunctionInvoker>();
            // _applicationLifetime = applicationLifetime;
            // _workerInitializationTimeout = workerInitializationTimeout;

            // InitializeFileWatcherIfEnabled();

            if (_outputBindings.Any(p => p.Metadata.IsReturn))
            {
                _handleScriptReturnValue = HandleReturnParameter;
            }
            else
            {
                _handleScriptReturnValue = HandleOutputDictionary;
            }
        }

        public ILogger FunctionLogger => throw new NotImplementedException();

        public Task<object> Invoke(object[] parameters)
        {
            FunctionInvocationContext context = GetContextFromParameters(parameters, _metadata);
            return InvokeCore(parameters, context);
        }

        private static FunctionInvocationContext GetContextFromParameters(object[] parameters, FunctionMetadata metadata)
        {
            ExecutionContext functionExecutionContext = null;
            Binder binder = null;
            ILogger logger = null;

            for (var i = 0; i < parameters.Length; i++)
            {
                switch (parameters[i])
                {
                    case ExecutionContext fc:
                        functionExecutionContext ??= fc;
                        break;
                    case Binder b:
                        binder ??= b;
                        break;
                    case ILogger l:
                        logger ??= l;
                        break;
                }
            }

            // We require the ExecutionContext, so this will throw if one is not found.
            if (functionExecutionContext == null)
            {
                throw new ArgumentException("Function ExecutionContext was not found");
            }

            functionExecutionContext.FunctionDirectory = metadata.FunctionDirectory;
            functionExecutionContext.FunctionName = metadata.Name;

            FunctionInvocationContext context = new FunctionInvocationContext
            {
                ExecutionContext = functionExecutionContext,
                Binder = binder,
                Logger = logger
            };

            return context;
        }

        private async Task<object> InvokeCore(object[] parameters, FunctionInvocationContext context)
        {
            // Need to wait for at least one language worker process to be initialized before accepting invocations
            // if (!IsDispatcherReady())
            // {
            //     await DelayUntilFunctionDispatcherInitializedOrShutdown();
            // }

            var triggerParameterIndex = 0;
            // var cancellationTokenParameterIndex = 4;
            var bindingData = context.Binder.BindingData;
            object triggerValue = TransformInput(parameters[triggerParameterIndex], bindingData);
            var triggerInput = (_bindingMetadata.Name, _bindingMetadata.DataType ?? DataType.String, triggerValue);
            IEnumerable<(string, DataType, object)> inputs = new[] { triggerInput };
            if (_inputBindings.Count > 1)
            {
                var nonTriggerInputs = await BindInputsAsync(context.Binder);
                inputs = inputs.Concat(nonTriggerInputs);
            }

            var invocationContext = new ScriptInvocationContext
            {
                FunctionMetadata = _metadata,
                BindingData = bindingData,
                ExecutionContext = context.ExecutionContext,
                Inputs = inputs,
                ResultSource = new TaskCompletionSource<ScriptInvocationResult>(),
                AsyncExecutionContext = System.Threading.ExecutionContext.Capture(),
                Traceparent = Activity.Current?.Id,
                Tracestate = Activity.Current?.TraceStateString,
                Attributes = Activity.Current?.Tags,
                // CancellationToken = HandleCancellationTokenParameter(parameters[cancellationTokenParameterIndex]),
                Logger = context.Logger
            };

            string invocationId = context.ExecutionContext.InvocationId.ToString();
            ScriptInvocationResult result = new();

            _logger.LogTrace("Sending invocation id: '{id}", invocationId);
            // await _functionDispatcher.InvokeAsync(invocationContext);

            var worker = _workerResolver.ResolveWorker(string.Empty) ?? throw new InvalidOperationException("No worker is available to process the function invocation.");
            await worker.InvokeAsync(invocationContext);

            try
            {
                result = await invocationContext.ResultSource.Task;
            }
            catch (OperationCanceledException ex)
            {
                // Only catch the exception when the task is cancelled, otherwise let it be handled by the ExceptionMiddleware
                throw new FunctionInvocationCanceledException(invocationId, ex);
            }

            await BindOutputsAsync(triggerValue, context.Binder, result);

            return result.Return;
        }

        //private bool IsDispatcherReady()
        //{
        //    return _workerResolver.State == FunctionInvocationDispatcherState.Initialized || _workerResolver.State == FunctionInvocationDispatcherState.Default;
        //}

        //private async Task DelayUntilFunctionDispatcherInitializedOrShutdown()
        //{
        //    // Don't delay if functionDispatcher is already initialized OR is skipping initialization for one of
        //    // these reasons: started in placeholder, has no functions, functions do not match set language.

        //    if (!IsDispatcherReady())
        //    {
        //        _logger.LogTrace($"FunctionDispatcher state: {_workerResolver.State}");
        //        bool result = await Utility.DelayAsync((_workerResolver.ErrorEventsThreshold + 1) * (int)_workerInitializationTimeout.TotalSeconds, WorkerConstants.WorkerReadyCheckPollingIntervalMilliseconds, () =>
        //        {
        //            return _workerResolver.State != FunctionInvocationDispatcherState.Initialized;
        //        });

        //        if (result)
        //        {
        //            _logger.LogError($"Final functionDispatcher state: {_workerResolver.State}. Initialization timed out and host is shutting down");
        //            _applicationLifetime.StopApplication();
        //        }
        //    }
        //}

        private async Task<(string Name, DataType Type, object Value)[]> BindInputsAsync(Binder binder)
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
            ApplyBindingData(input, bindingData);
            return input;
        }

        /// <summary>
        /// Applies any additional binding data from the input value to the specified binding data.
        /// This binding data then becomes available to the binding process (in the case of late bound bindings).
        /// </summary>
        internal static void ApplyBindingData(object value, Dictionary<string, object> bindingData)
        {
            try
            {
                // if the input value is a JSON string, extract additional
                // binding data from it
                string json = value as string;
                if (!string.IsNullOrEmpty(json) && Utility.IsJson(json))
                {
                    // parse the object adding top level properties
                    JObject parsed = JObject.Parse(json);
                    var additionalBindingData = parsed.Children<JProperty>()
                        .Where(p => p.Value != null && (p.Value.Type != JTokenType.Array))
                        .ToDictionary(p => p.Name, p => ConvertPropertyValue(p));

                    if (additionalBindingData != null)
                    {
                        foreach (var item in additionalBindingData)
                        {
                            if (item.Value != null)
                            {
                                bindingData[item.Key] = item.Value;
                            }
                        }
                    }
                }
            }
            catch
            {
                // it's not an error if the incoming message isn't JSON
                // there are cases where there will be output binding parameters
                // that don't bind to JSON properties
            }
        }

        private static object ConvertPropertyValue(JProperty property)
        {
            if (property.Value != null && property.Value.Type == JTokenType.Object)
            {
                return (JObject)property.Value;
            }
            else
            {
                return (string)property.Value;
            }
        }

        private CancellationToken HandleCancellationTokenParameter(object input)
        {
            if (input == null)
            {
                return CancellationToken.None;
            }

            return (CancellationToken)input;
        }

        private void HandleReturnParameter(ScriptInvocationResult result)
        {
            if (result.Outputs is IImmutableDictionary<string, object> immutableOutputs)
            {
                return;
            }

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

        public void OnError(Exception ex)
        {
            throw new NotImplementedException();
        }
    }
}