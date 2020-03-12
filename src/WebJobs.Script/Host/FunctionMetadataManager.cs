// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Logging;
using Microsoft.Azure.WebJobs.Script.Abstractions.Description;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Diagnostics.Extensions;
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
        private readonly IServiceProvider _serviceProvider;
        private readonly IFunctionMetadataProvider _functionMetadataProvider;
        private bool _isHttpWorker;
        private bool _servicesReset = false;
        private ILogger _logger;
        private IOptions<ScriptJobHostOptions> _scriptOptions;
        private ImmutableArray<FunctionMetadata> _functionMetadataFilteredArray;
        private ImmutableArray<FunctionMetadata> _functionMetadataAllArray;
        private IEnumerable<IFunctionProvider> _functionProviders;
        private Dictionary<string, ICollection<string>> _functionErrors = new Dictionary<string, ICollection<string>>();

        public FunctionMetadataManager(IOptions<ScriptJobHostOptions> scriptOptions, IFunctionMetadataProvider functionMetadataProvider, IEnumerable<IFunctionProvider> functionProviders, IOptions<HttpWorkerOptions> httpWorkerOptions, IScriptHostManager scriptHostManager, ILoggerFactory loggerFactory)
        {
            _scriptOptions = scriptOptions;
            _serviceProvider = scriptHostManager as IServiceProvider;
            _functionMetadataProvider = functionMetadataProvider;
            _logger = loggerFactory.CreateLogger(LogCategories.Startup);
            _isHttpWorker = httpWorkerOptions?.Value?.Description != null;
            _functionProviders = functionProviders;

            scriptHostManager.HostInitializing += ResetScriptHostServices;
        }

        public ImmutableDictionary<string, ImmutableArray<string>> Errors { get; private set; }

        /// <summary>
        /// Gets the function metadata array from all providers.
        /// </summary>
        /// <param name="forceRefresh">Forces reload from all providers.</param>
        /// <param name="includeBlocked">Includes all functions, including blocked ones.</param>
        /// <returns>An Immmutable array of FunctionMetadata.</returns>
        public ImmutableArray<FunctionMetadata> GetFunctionMetadata(bool forceRefresh, bool includeBlocked = false)
        {
            var currentlyLoaded = GetLoadedMetadata(includeBlocked);
            if (!forceRefresh && !_servicesReset && !currentlyLoaded.IsDefaultOrEmpty)
            {
                return currentlyLoaded;
            }

            LoadFunctionMetadata(forceRefresh);
            _servicesReset = false;

            return GetLoadedMetadata(includeBlocked);
        }

        internal ImmutableArray<FunctionMetadata> GetLoadedMetadata(bool includeBlocked)
        {
            return includeBlocked ? _functionMetadataAllArray : _functionMetadataFilteredArray;
        }

        private void ResetScriptHostServices(object sender, EventArgs e)
        {
            _functionProviders = _serviceProvider?.GetService<IEnumerable<IFunctionProvider>>();
            _isHttpWorker = _serviceProvider?.GetService<IOptions<HttpWorkerOptions>>()?.Value?.Description != null;
            _scriptOptions = _serviceProvider?.GetService<IOptions<ScriptJobHostOptions>>();

            // Resetting the logger switches the logger scope to Script Host level,
            // also making the logs available to Application Insights
            _logger = _serviceProvider?.GetService<ILoggerFactory>().CreateLogger(LogCategories.Startup);
            _servicesReset = true;
        }

        /// <summary>
        /// Read all functions and populate function metadata.
        /// </summary>
        internal void LoadFunctionMetadata(bool forceRefresh = false)
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
            if (_functionProviders != null && _functionProviders.Any())
            {
                AddMetadataFromCustomProviders(functionMetadataList);
                AddErrorsFromCustomProviders();
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
            var functionMetadataFilteredList = functionMetadataList;

            if (functionsWhiteList != null)
            {
                _logger.LogInformation($"A function whitelist has been specified, excluding all but the following functions: [{string.Join(", ", functionsWhiteList)}]");
                functionMetadataFilteredList = functionMetadataList.Where(function => functionsWhiteList.Any(functionName => functionName.Equals(function.Name, StringComparison.CurrentCultureIgnoreCase))).ToList();
                Errors = _functionErrors.Where(kvp => functionsWhiteList.Any(functionName => functionName.Equals(kvp.Key, StringComparison.CurrentCultureIgnoreCase))).ToImmutableDictionary(kvp => kvp.Key, kvp => kvp.Value.ToImmutableArray());
            }
            _logger.FunctionMetadataManagerFunctionsLoaded(functionMetadataFilteredList.Count());

            _functionMetadataAllArray = functionMetadataList.ToImmutableArray();
            _functionMetadataFilteredArray = functionMetadataFilteredList.ToImmutableArray();
        }

        internal bool IsScriptFileDetermined(FunctionMetadata functionMetadata)
        {
            try
            {
                if (string.IsNullOrEmpty(functionMetadata.ScriptFile) && !_isHttpWorker && !functionMetadata.IsProxy)
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

        private void AddMetadataFromCustomProviders(List<FunctionMetadata> functionMetadataList)
        {
            var functionProviderTasks = new List<Task<ImmutableArray<FunctionMetadata>>>();
            foreach (var functionProvider in _functionProviders)
            {
                functionProviderTasks.Add(functionProvider.GetFunctionMetadataAsync());
            }

            var functionMetadataListArray = Task.WhenAll(functionProviderTasks).GetAwaiter().GetResult();

            foreach (var someArray in functionMetadataListArray)
            {
                if (!someArray.IsDefaultOrEmpty)
                {
                    someArray.ToList().ForEach(el => functionMetadataList.Add(el));
                }
            }
        }

        private void AddErrorsFromCustomProviders()
        {
            foreach (var provider in _functionProviders)
            {
                provider.FunctionErrors?.ToList().ForEach(kvp => _functionErrors[kvp.Key] = kvp.Value.ToList());
            }
        }
    }
}