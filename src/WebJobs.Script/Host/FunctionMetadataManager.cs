// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Logging;
using Microsoft.Azure.WebJobs.Script.Configuration;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Diagnostics.Extensions;
using Microsoft.Azure.WebJobs.Script.Workers.Http;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.WebJobs.Script
{
    public sealed class FunctionMetadataManager : IFunctionMetadataManager, IDisposable
    {
        private const string FunctionConfigurationErrorMessage = "Unable to determine the primary function script. Make sure at least one script file is present. Try renaming your entry point script to 'run' or alternatively you can specify the name of the entry point script explicitly by adding a 'scriptFile' property to your function metadata.";
        private const string MetadataProviderName = "Custom";
        private readonly IServiceProvider _serviceProvider;
        private readonly IFunctionMetadataProvider _functionMetadataProvider;
        private readonly IEnvironment _environment;

        private LanguageWorkerOptions _languageOptions;
        private IDisposable _onChangeSubscription;
        private IOptions<ScriptJobHostOptions> _scriptOptions;
        private ILogger _logger;
        private bool _isHttpWorker;
        private bool _servicesReset = false;
        private ImmutableArray<FunctionMetadata> _functionMetadataArray;
        private Dictionary<string, ICollection<string>> _functionErrors = new Dictionary<string, ICollection<string>>();
        private ConcurrentDictionary<string, FunctionMetadata> _functionMetadataMap = new ConcurrentDictionary<string, FunctionMetadata>(StringComparer.OrdinalIgnoreCase);

        public FunctionMetadataManager(
            IOptions<ScriptJobHostOptions> scriptOptions,
            IFunctionMetadataProvider functionMetadataProvider,
            IOptions<HttpWorkerOptions> httpWorkerOptions,
            IScriptHostManager scriptHostManager,
            ILoggerFactory loggerFactory,
            IEnvironment environment,
            IOptionsMonitor<LanguageWorkerOptions> languageOptions)
        {
            _scriptOptions = scriptOptions;
            _serviceProvider = scriptHostManager as IServiceProvider;
            _functionMetadataProvider = functionMetadataProvider;
            _logger = loggerFactory.CreateLogger(LogCategories.Startup);
            _isHttpWorker = httpWorkerOptions?.Value?.Description != null;
            _environment = environment;

            InitializeLanguageOptions(languageOptions);

            // Every time script host is re-initializing, we also need to re-initialize
            // services that change with the scope of the script host.
            scriptHostManager.ActiveHostChanged += (s, e) =>
            {
                if (e.NewHost is not null)
                {
                    InitializeServices();
                }
            };
        }

        /// <inheritdoc />
        public ImmutableDictionary<string, ImmutableArray<string>> Errors { get; private set; }

        /// <inheritdoc />
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
        /// <param name="includeCustomProviders">Include any metadata provided by IFunctionProvider when loading the metadata.</param>
        /// <returns> An Immutable array of FunctionMetadata.</returns>
        public ImmutableArray<FunctionMetadata> GetFunctionMetadata(bool forceRefresh, bool applyAllowList = true, bool includeCustomProviders = true)
        {
            if (forceRefresh || _servicesReset || _functionMetadataArray.IsDefaultOrEmpty)
            {
                _functionMetadataArray = LoadFunctionMetadata(forceRefresh, includeCustomProviders);
                _logger.FunctionMetadataManagerFunctionsLoaded(ApplyAllowList(_functionMetadataArray).Count());
                _servicesReset = false;
            }

            return applyAllowList ? ApplyAllowList(_functionMetadataArray) : _functionMetadataArray;
        }

        /// <inheritdoc />
        public void Dispose() => _onChangeSubscription.Dispose();

        private void InitializeLanguageOptions(IOptionsMonitor<LanguageWorkerOptions> options)
        {
            _onChangeSubscription?.Dispose();
            _languageOptions = options.CurrentValue;
            _onChangeSubscription = options.OnChange(o =>
            {
                _languageOptions = o;
                _servicesReset = true;
            });
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

            _onChangeSubscription.Dispose();
            InitializeLanguageOptions(_serviceProvider.GetService<IOptionsMonitor<LanguageWorkerOptions>>());
            _servicesReset = true;
        }

        /// <summary>
        /// Read all functions and populate function metadata.
        /// </summary>
        internal ImmutableArray<FunctionMetadata> LoadFunctionMetadata(bool forceRefresh = false, bool includeCustomProviders = true)
        {
            var workerConfigs = _languageOptions.WorkerConfigs;
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

            if (functionMetadataList.Count == 0 && !_environment.IsPlaceholderModeEnabled())
            {
                // Validate the host.json file if no functions are found.
                ValidateHostJsonFile();
            }

            return functionMetadataList.OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase).ToImmutableArray();
        }

        internal bool IsScriptFileDetermined(FunctionMetadata functionMetadata)
        {
            try
            {
                if (string.IsNullOrEmpty(functionMetadata.ScriptFile) && !_isHttpWorker && !functionMetadata.IsProxy() && _servicesReset)
                {
                    throw new FunctionConfigurationException(FunctionConfigurationErrorMessage);
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
            _logger.ReadingFunctionMetadataFromProvider(MetadataProviderName);

            var functionProviderTasks = new List<Task<ImmutableArray<FunctionMetadata>>>();
            var metadataProviderTimeout = _scriptOptions.Value.MetadataProviderTimeout;

            foreach (var functionProvider in functionProviders)
            {
                var getFunctionMetadataFromProviderTask = functionProvider.GetFunctionMetadataAsync();
                var delayTask = Task.Delay(metadataProviderTimeout);

                var completedTask = Task.WhenAny(getFunctionMetadataFromProviderTask, delayTask).ContinueWith(t =>
                {
                    if (t.Result == getFunctionMetadataFromProviderTask && getFunctionMetadataFromProviderTask.IsCompletedSuccessfully)
                    {
                        return getFunctionMetadataFromProviderTask.Result;
                    }

                    // Timeout case.
                    throw new TimeoutException($"Timeout occurred while retrieving metadata from provider '{functionProvider.GetType().FullName}'. The operation exceeded the configured timeout of {metadataProviderTimeout.TotalSeconds} seconds.");
                });

                functionProviderTasks.Add(completedTask);
            }

            var providerFunctionMetadataResults = Task.WhenAll(functionProviderTasks).GetAwaiter().GetResult();
            var totalFunctionsCount = providerFunctionMetadataResults.Where(metadataArray => !metadataArray.IsDefaultOrEmpty).Sum(metadataArray => metadataArray.Length);

            // This is used to make sure no duplicates are registered
            var distinctFunctionNames = new HashSet<string>(functionMetadataList.Select(m => m.Name));

            _logger.FunctionsReturnedByProvider(totalFunctionsCount, MetadataProviderName);

            foreach (var metadataArray in providerFunctionMetadataResults)
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

        private void ValidateHostJsonFile()
        {
            try
            {
                if (_scriptOptions.Value.RootScriptPath is not null && _scriptOptions.Value.IsDefaultHostConfig)
                {
                    // Search for the host.json file within nested directories to verify scenarios where it isn't located at the root. This situation often occurs when a function app has been improperly zipped.
                    string hostFilePath = Path.Combine(_scriptOptions.Value.RootScriptPath, ScriptConstants.HostMetadataFileName);
                    IEnumerable<string> hostJsonFiles = Directory.GetFiles(_scriptOptions.Value.RootScriptPath, ScriptConstants.HostMetadataFileName, SearchOption.AllDirectories)
                        .Where(file => !file.Equals(hostFilePath, StringComparison.OrdinalIgnoreCase));

                    if (hostJsonFiles != null && hostJsonFiles.Any())
                    {
                        string hostJsonFilesPath = string.Join(", ", hostJsonFiles).Replace(_scriptOptions.Value.RootScriptPath, string.Empty);
                        _logger.HostJsonZipDeploymentIssue(hostJsonFilesPath);
                    }
                    else
                    {
                        _logger.NoHostJsonFile();
                    }
                }
            }
            catch
            {
                // Ignore any exceptions.
            }
        }
    }
}