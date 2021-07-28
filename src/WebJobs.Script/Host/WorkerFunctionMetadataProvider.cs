// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using System.Reactive.Linq;
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
        private readonly IOptionsMonitor<ScriptApplicationHostOptions> _applicationHostOptions;
        private readonly IMetricsLogger _metricsLogger;
        private readonly Dictionary<string, ICollection<string>> _functionErrors = new Dictionary<string, ICollection<string>>();
        private readonly ILogger _logger;
        private ImmutableArray<FunctionMetadata> _functions;

        public WorkerFunctionMetadataProvider(IOptionsMonitor<ScriptApplicationHostOptions> applicationHostOptions, ILogger<WorkerFunctionMetadataProvider> logger, IMetricsLogger metricsLogger)
        {
            _applicationHostOptions = applicationHostOptions;
            _metricsLogger = metricsLogger;
            _logger = logger;
        }

        public ImmutableDictionary<string, ImmutableArray<string>> FunctionErrors
           => _functionErrors.ToImmutableDictionary(kvp => kvp.Key, kvp => kvp.Value.ToImmutableArray());

        public ImmutableArray<FunctionMetadata> GetFunctionMetadata(IEnumerable<RpcWorkerConfig> workerConfigs, bool forceRefresh, IFunctionInvocationDispatcher dispatcher = null)
        {
            return GetFunctionMetadataAsync(workerConfigs, forceRefresh, dispatcher).Result;
        }

        internal async Task<ImmutableArray<FunctionMetadata>> GetFunctionMetadataAsync(IEnumerable<RpcWorkerConfig> workerConfigs, bool forceRefresh, IFunctionInvocationDispatcher dispatcher = null)
        {
            IEnumerable<FunctionMetadata> functions = new List<FunctionMetadata>();
            List<FunctionMetadata> validatedFunctions;
            _logger.FunctionMetadataProviderParsingFunctions();
            if (_functions.IsDefaultOrEmpty || forceRefresh)
            {
                if (dispatcher != null)
                {
                    await dispatcher.InitializeAsync(null);
                    functions = await dispatcher.GetWorkerMetadata();
                    functions = ValidateMetadata(functions);
                    if (functions.Count() == 0)
                    {
                        FunctionMetadata one = new FunctionMetadata()
                        {
                            Name = "hello there",
                            FunctionDirectory = "somethingRandom"
                        };
                        validatedFunctions = functions.ToList();
                        validatedFunctions.Add(one);
                        functions = validatedFunctions;
                    }
                    await dispatcher.FinishInitialization(functions);
                }
            }
            _logger.FunctionMetadataProviderFunctionFound(functions.Count());
            _functions = functions.ToImmutableArray();
            return _functions;
        }

        internal IEnumerable<FunctionMetadata> ValidateMetadata(IEnumerable<FunctionMetadata> functions)
        {
            if (functions == null)
            {
                _logger.LogError("There is no metadata to be validated.");
                return null;
            }
            _functionErrors.Clear();
            List<FunctionMetadata> validatedMetadata = new List<FunctionMetadata>();
            foreach (FunctionMetadata function in functions)
            {
                // function name validation
                try
                {
                    ValidateName(function.Name);

                    // function directory validation
                    IFileSystem fileSystem = FileUtility.Instance;
                    if (!Utility.TryReadFunctionConfig(function.FunctionDirectory, out string json, fileSystem))
                    {
                        // not a function directory
                        continue;
                    }

                    // skip function ScriptFile validation for now because this involves enumerating file directory

                    // language validation
                    if (!ValidateLanguage(function.Language))
                    {
                        continue;
                    }

                    // retry option validation
                    Utility.ValidateRetryOptions(function.Retry);

                    // binding validation
                    if (function.Bindings == null || function.Bindings.Count == 0)
                    {
                        throw new FormatException("At least one binding must be declared.");
                    }

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

        internal bool ValidateLanguage(string language)
        {
            switch (language)
            {
                case RpcWorkerConstants.DotNetLanguageWorkerName:
                    return true;
                case RpcWorkerConstants.NodeLanguageWorkerName:
                    return true;
                case RpcWorkerConstants.JavaLanguageWorkerName:
                    return true;
                case RpcWorkerConstants.PythonLanguageWorkerName:
                    return true;
                case RpcWorkerConstants.PowerShellLanguageWorkerName:
                    return true;
                default:
                    return false;
            }
        }

        internal void ValidateName(string name, bool isProxy = false)
        {
            if (!Utility.IsValidFunctionName(name))
            {
                throw new InvalidOperationException(string.Format("'{0}' is not a valid {1} name.", name, isProxy ? "proxy" : "function"));
            }
        }
    }
}
