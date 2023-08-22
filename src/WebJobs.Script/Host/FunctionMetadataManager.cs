// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Logging;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Diagnostics.Extensions;
using Microsoft.Azure.WebJobs.Script.Workers;
using Microsoft.Azure.WebJobs.Script.Workers.Http;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.WebJobs.Script
{
    public class FunctionMetadataManager : IFunctionMetadataManager
    {
        private const string _functionConfigurationErrorMessage = "Unable to determine the primary function script.Make sure atleast one script file is present.Try renaming your entry point script to 'run' or alternatively you can specify the name of the entry point script explicitly by adding a 'scriptFile' property to your function metadata.";
        private const string _metadataProviderName = "Custom";
        private readonly IServiceProvider _serviceProvider;
        private readonly ILoggerFactory _loggerFactory;
        private IFunctionMetadataProvider _functionMetadataProvider;
        private bool _isHttpWorker;
        private IEnvironment _environment;
        private bool _servicesReset = false;
        private ILogger _logger;
        private IOptions<ScriptJobHostOptions> _scriptOptions;
        private ImmutableArray<FunctionMetadata> _functionMetadataArray;
        private Dictionary<string, ICollection<string>> _functionErrors = new Dictionary<string, ICollection<string>>();
        private ConcurrentDictionary<string, FunctionMetadata> _functionMetadataMap = new ConcurrentDictionary<string, FunctionMetadata>(StringComparer.OrdinalIgnoreCase);

        public FunctionMetadataManager(IOptions<ScriptJobHostOptions> scriptOptions, IFunctionMetadataProvider functionMetadataProvider,
            IOptions<HttpWorkerOptions> httpWorkerOptions, IScriptHostManager scriptHostManager, ILoggerFactory loggerFactory,
            IEnvironment environment)
        {
            _scriptOptions = scriptOptions;
            _serviceProvider = scriptHostManager as IServiceProvider;
            _functionMetadataProvider = functionMetadataProvider;
            _loggerFactory = loggerFactory;
            _logger = loggerFactory.CreateLogger(LogCategories.Startup);
            _isHttpWorker = httpWorkerOptions?.Value?.Description != null;
            _environment = environment;

            // Every time script host is re-intializing, we also need to re-initialize
            // services that change with the scope of the script host.
            scriptHostManager.ActiveHostChanged += (s, e) =>
            {
                if (e.NewHost is not null)
                {
                    InitializeServices();
                }
            };
        }

        public ImmutableDictionary<string, ImmutableArray<string>> Errors { get; private set; }

        public bool TryGetFunctionMetadata(string functionName, out FunctionMetadata functionMetadata, bool forceRefresh)
        {
            if (forceRefresh)
            {
                _functionMetadataMap.Clear();
            }

            functionMetadata = _functionMetadataMap.GetOrAdd(functionName, s =>
            {
                var functions = GetFunctionMetadata(false);
                return functions.FirstOrDefault(p => Utility.FunctionNamesMatch(p.Name, s));
            });

            return functionMetadata != null;
        }

        /// <summary>
        /// Gets the function metadata array from all providers.
        /// </summary>
        /// <param name="forceRefresh">Forces reload from all providers.</param>
        /// <param name="applyAllowList">Apply functions allow list filter.</param>
        /// <param name="includeCustomProviders">Include any metadata provided by IFunctionProvider when loading the metadata</param>
        /// <returns> An Immmutable array of FunctionMetadata.</returns>
        public ImmutableArray<FunctionMetadata> GetFunctionMetadata(bool forceRefresh, bool applyAllowList = true, bool includeCustomProviders = true, IList<RpcWorkerConfig> workerConfigs = null)
        {
            if (forceRefresh || _servicesReset || _functionMetadataArray.IsDefaultOrEmpty)
            {
                _functionMetadataArray = LoadFunctionMetadata(forceRefresh, includeCustomProviders, workerConfigs: workerConfigs);
                _logger.FunctionMetadataManagerFunctionsLoaded(ApplyAllowList(_functionMetadataArray).Count());
                _servicesReset = false;
            }

            return applyAllowList ? ApplyAllowList(_functionMetadataArray) : _functionMetadataArray;
        }

        private ImmutableArray<FunctionMetadata> ApplyAllowList(ImmutableArray<FunctionMetadata> metadataList)
        {
            var allowList = _scriptOptions.Value?.Functions;

            if (allowList == null || metadataList.IsDefaultOrEmpty)
            {
                return metadataList;
            }

            return metadataList.Where(metadata => allowList.Any(functionName => functionName.Equals(metadata.Name, StringComparison.CurrentCultureIgnoreCase))).ToImmutableArray();
        }

        private void InitializeServices()
        {
            _functionMetadataMap.Clear();

            _isHttpWorker = _serviceProvider.GetService<IOptions<HttpWorkerOptions>>()?.Value?.Description != null;
            _scriptOptions = _serviceProvider.GetService<IOptions<ScriptJobHostOptions>>();

            // Resetting the logger switches the logger scope to Script Host level,
            // also making the logs available to Application Insights
            _logger = _serviceProvider?.GetService<ILoggerFactory>().CreateLogger(LogCategories.Startup);
            _servicesReset = true;
        }

        /// <summary>
        /// This is the worker configuration created in the jobhost scope during placeholder initialization
        /// This is used as a fallback incase the config is not passed down from previous method call.
        /// </summary>
        private IList<RpcWorkerConfig> GetFallbackWorkerConfig()
        {
            return _serviceProvider.GetService<IOptionsMonitor<LanguageWorkerOptions>>().CurrentValue.WorkerConfigs;
        }

        /// <summary>
        /// Read all functions and populate function metadata.
        /// </summary>
        internal ImmutableArray<FunctionMetadata> LoadFunctionMetadata(bool forceRefresh = false, bool includeCustomProviders = true, IFunctionInvocationDispatcher dispatcher = null, IList<RpcWorkerConfig> workerConfigs = null)
        {
            workerConfigs ??= GetFallbackWorkerConfig();

            _functionMetadataMap.Clear();

            ICollection<string> functionsAllowList = _scriptOptions?.Value?.Functions;
            _logger.FunctionMetadataManagerLoadingFunctionsMetadata();

            ImmutableArray<FunctionMetadata> immutableFunctionMetadata;

            immutableFunctionMetadata = _functionMetadataProvider.GetFunctionMetadataAsync(workerConfigs, _environment, forceRefresh).GetAwaiter().GetResult();

            var functionMetadataList = new List<FunctionMetadata>();
            _functionErrors = new Dictionary<string, ICollection<string>>();

            if (!immutableFunctionMetadata.IsDefaultOrEmpty)
            {
                functionMetadataList.AddRange(immutableFunctionMetadata);
            }

            if (!_functionMetadataProvider.FunctionErrors?.IsEmpty ?? false)
            {
                _functionErrors = _functionMetadataProvider.FunctionErrors.ToDictionary(kvp => kvp.Key, kvp => (ICollection<string>)kvp.Value.ToList());
            }

            // Add metadata and errors from any additional function providers
            if (includeCustomProviders)
            {
                LoadCustomProviderFunctions(functionMetadataList);
            }

            // Validate
            foreach (FunctionMetadata functionMetadata in functionMetadataList.ToList())
            {
                if (!IsScriptFileDetermined(functionMetadata))
                {
                    // Exclude invalid functions
                    functionMetadataList.Remove(functionMetadata);
                }
            }
            Errors = _functionErrors.ToImmutableDictionary(kvp => kvp.Key, kvp => kvp.Value.ToImmutableArray());

            if (functionsAllowList != null)
            {
                _logger.LogInformation($"A function allow list has been specified, excluding all but the following functions: [{string.Join(", ", functionsAllowList)}]");
                Errors = _functionErrors.Where(kvp => functionsAllowList.Any(functionName => functionName.Equals(kvp.Key, StringComparison.CurrentCultureIgnoreCase))).ToImmutableDictionary(kvp => kvp.Key, kvp => kvp.Value.ToImmutableArray());
            }

            return functionMetadataList.OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase).ToImmutableArray();
        }

        internal bool IsScriptFileDetermined(FunctionMetadata functionMetadata)
        {
            try
            {
                if (string.IsNullOrEmpty(functionMetadata.ScriptFile) && !_isHttpWorker && !functionMetadata.IsProxy() && _servicesReset)
                {
                    throw new FunctionConfigurationException(_functionConfigurationErrorMessage);
                }
            }
            catch (FunctionConfigurationException exc)
            {
                // for functions in error, log the error and don't
                // add to the functions collection
                Utility.AddFunctionError(_functionErrors, functionMetadata.Name, exc.Message);
                return false;
            }
            return true;
        }

        private void LoadCustomProviderFunctions(List<FunctionMetadata> functionMetadataList)
        {
            // We always want to get the most updated function providers in case this list was changed.
            IEnumerable<IFunctionProvider> functionProviders = _serviceProvider?.GetService<IEnumerable<IFunctionProvider>>();

            if (functionProviders != null && functionProviders.Any())
            {
                AddMetadataFromCustomProviders(functionProviders, functionMetadataList);
            }
        }

        private void AddMetadataFromCustomProviders(IEnumerable<IFunctionProvider> functionProviders, List<FunctionMetadata> functionMetadataList)
        {
            _logger.ReadingFunctionMetadataFromProvider(_metadataProviderName);

            var functionProviderTasks = new List<Task<ImmutableArray<FunctionMetadata>>>();
            foreach (var functionProvider in functionProviders)
            {
                functionProviderTasks.Add(functionProvider.GetFunctionMetadataAsync());
            }

            var functionMetadataListArray = Task.WhenAll(functionProviderTasks).GetAwaiter().GetResult();

            // This is used to make sure no duplicates are registered
            var distinctFunctionNames = new HashSet<string>(functionMetadataList.Select(m => m.Name));

            _logger.FunctionsReturnedByProvider(functionMetadataListArray.Length, _metadataProviderName);

            foreach (var metadataArray in functionMetadataListArray)
            {
                if (!metadataArray.IsDefaultOrEmpty)
                {
                    foreach (var metadata in metadataArray)
                    {
                        if (distinctFunctionNames.Contains(metadata.Name))
                        {
                            throw new InvalidOperationException($"Found duplicate {nameof(FunctionMetadata)} with the name {metadata.Name}");
                        }

                        // If not explicitly set, consider the function codeless.
                        if (!metadata.IsCodelessSet())
                        {
                            metadata.SetIsCodeless(true);
                        }

                        distinctFunctionNames.Add(metadata.Name);
                        functionMetadataList.Add(metadata);
                    }
                }
            }

            foreach (var functionProvider in functionProviders)
            {
                if (functionProvider.FunctionErrors == null)
                {
                    continue;
                }

                foreach (var errorKvp in functionProvider.FunctionErrors)
                {
                    _functionErrors[errorKvp.Key] = errorKvp.Value.ToList();
                }
            }
        }
    }
}