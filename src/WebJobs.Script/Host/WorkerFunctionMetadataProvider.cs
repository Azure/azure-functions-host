﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using System.Reactive.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Logging;
using Microsoft.Azure.WebJobs.Script.Configuration;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.Diagnostics.Extensions;
using Microsoft.Azure.WebJobs.Script.Workers;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json.Linq;

namespace Microsoft.Azure.WebJobs.Script
{
    public class WorkerFunctionMetadataProvider : IFunctionMetadataProvider
    {
        private static readonly Regex BindingNameValidationRegex = new Regex(string.Format("^([a-zA-Z][a-zA-Z0-9]{{0,127}}|{0})$", Regex.Escape(ScriptConstants.SystemReturnParameterBindingName)));
        private readonly Dictionary<string, ICollection<string>> _functionErrors = new Dictionary<string, ICollection<string>>();
        private readonly ILogger _logger;
        private readonly IFunctionInvocationDispatcher _dispatcher;
        private ImmutableArray<FunctionMetadata> _functions;

        public WorkerFunctionMetadataProvider(ILogger<WorkerFunctionMetadataProvider> logger, IFunctionInvocationDispatcher invocationDispatcher)
        {
            _logger = logger;
            _dispatcher = invocationDispatcher ?? throw new ArgumentNullException(nameof(invocationDispatcher));
        }

        public ImmutableDictionary<string, ImmutableArray<string>> FunctionErrors
           => _functionErrors.ToImmutableDictionary(kvp => kvp.Key, kvp => kvp.Value.ToImmutableArray());

        public async Task<ImmutableArray<FunctionMetadata>> GetFunctionMetadataAsync(IEnumerable<RpcWorkerConfig> workerConfigs, bool forceRefresh)
        {
            await Task.FromResult(0);
            return GetFunctionMetadataAsync(forceRefresh).GetAwaiter().GetResult();
        }

        internal async Task<ImmutableArray<FunctionMetadata>> GetFunctionMetadataAsync(bool forceRefresh)
        {
            IEnumerable<RawFunctionMetadata> rawFunctions = new List<RawFunctionMetadata>();
            IEnumerable<FunctionMetadata> functions = new List<FunctionMetadata>();
            _logger.FunctionMetadataProviderParsingFunctions();
            if (_functions.IsDefaultOrEmpty || forceRefresh)
            {
                if (_dispatcher != null)
                {
                    // start up GRPC channels
                    await _dispatcher.InitializeAsync(new List<FunctionMetadata>());

                    // get function metadata from worker, then validate it
                    rawFunctions = await _dispatcher.GetWorkerMetadata();
                    functions = ValidateMetadata(rawFunctions);

                    // set up invocation buffers and send load requests
                    await _dispatcher.FinishInitialization(functions);
                }
            }
            _logger.FunctionMetadataProviderFunctionFound(functions.Count());
            _functions = functions.ToImmutableArray();
            return _functions;
        }

        internal IEnumerable<FunctionMetadata> ValidateMetadata(IEnumerable<RawFunctionMetadata> functions)
        {
            List<FunctionMetadata> validatedMetadata = new List<FunctionMetadata>();
            if (functions == null || functions.Count() == 0)
            {
                _logger.LogDebug("There is no metadata to be validated.");
                return validatedMetadata;
            }
            _functionErrors.Clear();
            foreach (RawFunctionMetadata rawFunction in functions)
            {
                var function = rawFunction.Metadata;
                try
                {
                    ValidateName(function.Name);

                    function.Language = SystemEnvironment.Instance.GetEnvironmentVariable(RpcWorkerConstants.FunctionWorkerRuntimeSettingName);

                    // skip function directory validation because this involves reading function.json

                    // skip function ScriptFile validation for now because this involves enumerating file directory

                    // configuration source validation
                    if (!string.IsNullOrEmpty(rawFunction.ConfigurationSource))
                    {
                        JToken isDirect = JToken.Parse(rawFunction.ConfigurationSource);
                        var isDirectValue = isDirect?.ToString();
                        if (string.Equals(isDirectValue, "attributes", StringComparison.OrdinalIgnoreCase))
                        {
                            function.SetIsDirect(true);
                        }
                        else if (!string.Equals(isDirectValue, "config", StringComparison.OrdinalIgnoreCase))
                        {
                            throw new FormatException($"Illegal value '{isDirectValue}' for 'configurationSource' property in {function.Name}'.");
                        }
                    }

                    // retry option validation
                    if (!string.IsNullOrEmpty(rawFunction.RetryOptions))
                    {
                        function.Retry = JObject.Parse(rawFunction.RetryOptions).ToObject<RetryOptions>();
                        Utility.ValidateRetryOptions(function.Retry);
                    }

                    // binding validation
                    function = ValidateBindings(rawFunction.Bindings, function);

                    // add validated metadata to validated list if it gets this far
                    validatedMetadata.Add(function);
                }
                catch (Exception ex)
                {
                    Utility.AddFunctionError(_functionErrors, function.Name, Utility.FlattenException(ex, includeSource: false), isFunctionShortName: true);
                }
            }
            return validatedMetadata;
        }

        internal static void ValidateName(string name, bool isProxy = false)
        {
            if (!Utility.IsValidFunctionName(name))
            {
                throw new InvalidOperationException(string.Format("'{0}' is not a valid {1} name.", name, isProxy ? "proxy" : "function"));
            }
        }

        internal static FunctionMetadata ValidateBindings(IEnumerable<string> rawBindings, FunctionMetadata function)
        {
            HashSet<string> bindingNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (string binding in rawBindings)
            {
                var functionBinding = BindingMetadata.Create(JObject.Parse(binding));

                // validate binding name
                if (functionBinding.Name == null || !BindingNameValidationRegex.IsMatch(functionBinding.Name))
                {
                    throw new ArgumentException($"The binding name {functionBinding.Name} is invalid. Please assign a valid name to the binding.");
                }

                // validate that output binding has correct direction
                if (functionBinding.IsReturn && functionBinding.Direction != BindingDirection.Out)
                {
                    throw new ArgumentException($"{ScriptConstants.SystemReturnParameterBindingName} bindings must specify a direction of 'out'.");
                }

                // Ensure no duplicate binding names exist
                if (bindingNames.Contains(functionBinding.Name))
                {
                    throw new InvalidOperationException(string.Format("Multiple bindings with name '{0}' discovered. Binding names must be unique.", functionBinding.Name));
                }
                else
                {
                    bindingNames.Add(functionBinding.Name);
                }

                // add binding to function.Bindings once validation is complete
                function.Bindings.Add(functionBinding);
            }

            // ensure there is at least one binding after validation
            if (function.Bindings == null || function.Bindings.Count == 0)
            {
                throw new FormatException("At least one binding must be declared.");
            }

            // ensure that there is a trigger binding
            var triggerMetadata = function.InputBindings.FirstOrDefault(p => p.IsTrigger);
            if (triggerMetadata == null)
            {
                throw new InvalidOperationException("No trigger binding specified. A function must have a trigger input binding.");
            }

            return function;
        }
    }
}
