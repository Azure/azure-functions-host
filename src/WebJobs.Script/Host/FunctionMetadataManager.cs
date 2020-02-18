// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Logging;
using Microsoft.Azure.WebJobs.Script.Abstractions.Description;
using Microsoft.Azure.WebJobs.Script.Diagnostics.Extensions;
using Microsoft.Azure.WebJobs.Script.Workers.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.WebJobs.Script
{
    public class FunctionMetadataManager : IFunctionMetadataManager
    {
        private const string _functionConfigurationErrorMessage = "Unable to determine the primary function script.Make sure atleast one script file is present.Try renaming your entry point script to 'run' or alternatively you can specify the name of the entry point script explicitly by adding a 'scriptFile' property to your function metadata.";
        private readonly IServiceProvider _serviceProvider;
        private readonly IFunctionMetadataProvider _functionMetadataProvider;
        private bool _isHttpWorker;
        private bool _servicesReset = false;
        private ILogger _logger;
        private IOptions<ScriptJobHostOptions> _scriptOptions;
        private ImmutableArray<FunctionMetadata> _functionMetadataArray;
        private IEnumerable<IFunctionProvider> _functionProviders;
        private Dictionary<string, ICollection<string>> _functionErrors = new Dictionary<string, ICollection<string>>();

        public FunctionMetadataManager(IOptions<ScriptJobHostOptions> scriptOptions, IFunctionMetadataProvider functionMetadataProvider,
            IEnumerable<IFunctionProvider> functionProviders, IOptions<HttpWorkerOptions> httpWorkerOptions, IScriptHostManager scriptHostManager, ILoggerFactory loggerFactory)
        {
            _scriptOptions = scriptOptions;
            _serviceProvider = scriptHostManager as IServiceProvider;
            _functionMetadataProvider = functionMetadataProvider;

            _logger = loggerFactory.CreateLogger(LogCategories.Startup);
            _isHttpWorker = httpWorkerOptions?.Value?.Description != null;
            _functionProviders = functionProviders;

            // Every time script host is re-intializing, we also need to re-initialize
            // services that change with the scope of the script host.
            scriptHostManager.HostInitializing += (s, e) => InitializeServices();
        }

        public ImmutableDictionary<string, ImmutableArray<string>> Errors { get; private set; }

        /// <summary>
        /// Gets the function metadata array from all providers.
        /// </summary>
        /// <param name="forceRefresh">Forces reload from all providers.</param>
        /// <param name="applyWhitelist">Apply functions whitelist filter.</param>
        /// <returns> An Immmutable array of FunctionMetadata.</returns>
        public ImmutableArray<FunctionMetadata> GetFunctionMetadata(bool forceRefresh, bool applyWhitelist = true)
        {
            if (forceRefresh || _servicesReset || _functionMetadataArray.IsDefaultOrEmpty)
            {
                _functionMetadataArray = LoadFunctionMetadata(forceRefresh);
                _logger.FunctionMetadataManagerFunctionsLoaded(ApplyWhitelist(_functionMetadataArray).Count());
                _servicesReset = false;
            }

            return applyWhitelist ? ApplyWhitelist(_functionMetadataArray) : _functionMetadataArray;
        }

        private ImmutableArray<FunctionMetadata> ApplyWhitelist(ImmutableArray<FunctionMetadata> metadataList)
        {
            var whitelist = _scriptOptions.Value?.Functions;

            if (whitelist == null || metadataList.IsDefaultOrEmpty)
            {
                return metadataList;
            }

            return metadataList.Where(metadata => whitelist.Any(functionName => functionName.Equals(metadata.Name, StringComparison.CurrentCultureIgnoreCase))).ToImmutableArray();
        }

        private void InitializeServices()
        {
            _functionProviders = _serviceProvider.GetService<IEnumerable<IFunctionProvider>>();
            _isHttpWorker = _serviceProvider.GetService<IOptions<HttpWorkerOptions>>()?.Value?.Description != null;
            _scriptOptions = _serviceProvider.GetService<IOptions<ScriptJobHostOptions>>();

            // Resetting the logger switches the logger scope to Script Host level,
            // also making the logs available to Application Insights
            _logger = _serviceProvider?.GetService<ILoggerFactory>().CreateLogger(LogCategories.Startup);
            _servicesReset = true;
        }

        /// <summary>
        /// Read all functions and populate function metadata.
        /// </summary>
        internal ImmutableArray<FunctionMetadata> LoadFunctionMetadata(bool forceRefresh = false)
        {
            ICollection<string> functionsWhiteList = _scriptOptions?.Value?.Functions;
            _logger.FunctionMetadataManagerLoadingFunctionsMetadata();

            var immutableFunctionMetadata = _functionMetadataProvider.GetFunctionMetadata(forceRefresh);
            var functionMetadataList = new List<FunctionMetadata>();

            if (!immutableFunctionMetadata.IsDefaultOrEmpty)
            {
                functionMetadataList.AddRange(immutableFunctionMetadata);
            }

            if (!_functionMetadataProvider.FunctionErrors?.IsEmpty ?? false)
            {
                _functionErrors = _functionMetadataProvider.FunctionErrors.ToDictionary(kvp => kvp.Key, kvp => (ICollection<string>)kvp.Value.ToList());
            }

            // Add metadata and errors from any additional function providers
            LoadCustomProviderFunctions(functionMetadataList);

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

            if (functionsWhiteList != null)
            {
                _logger.LogInformation($"A function whitelist has been specified, excluding all but the following functions: [{string.Join(", ", functionsWhiteList)}]");
                Errors = _functionErrors.Where(kvp => functionsWhiteList.Any(functionName => functionName.Equals(kvp.Key, StringComparison.CurrentCultureIgnoreCase))).ToImmutableDictionary(kvp => kvp.Key, kvp => kvp.Value.ToImmutableArray());
            }

            return functionMetadataList.ToImmutableArray();
        }

        internal bool IsScriptFileDetermined(FunctionMetadata functionMetadata)
        {
            try
            {
                if (string.IsNullOrEmpty(functionMetadata.ScriptFile) && !_isHttpWorker && !functionMetadata.IsProxy())
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
            if (_functionProviders != null && _functionProviders.Any())
            {
                AddMetadataFromCustomProviders(functionMetadataList);
            }
        }

        private void AddMetadataFromCustomProviders(List<FunctionMetadata> functionMetadataList)
        {
            var functionProviderTasks = new List<Task<ImmutableArray<FunctionMetadata>>>();
            foreach (var functionProvider in _functionProviders)
            {
                functionProviderTasks.Add(functionProvider.GetFunctionMetadataAsync());
            }

            var functionMetadataListArray = Task.WhenAll(functionProviderTasks).GetAwaiter().GetResult();

            // This is used to make sure no duplicates are registered
            var distinctFunctionNames = new HashSet<string>(functionMetadataList.Select(m => m.Name));

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

                        // All custom provided functions are considered codeless functions
                        metadata.SetIsCodeless(true);

                        distinctFunctionNames.Add(metadata.Name);
                        functionMetadataList.Add(metadata);
                    }
                }
            }

            foreach (var functionProvider in _functionProviders)
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