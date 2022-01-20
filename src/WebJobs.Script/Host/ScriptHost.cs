﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reactive.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Host.Executors;
using Microsoft.Azure.WebJobs.Host.Indexers;
using Microsoft.Azure.WebJobs.Host.Listeners;
using Microsoft.Azure.WebJobs.Hosting;
using Microsoft.Azure.WebJobs.Logging;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Configuration;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.Diagnostics.Extensions;
using Microsoft.Azure.WebJobs.Script.Eventing;
using Microsoft.Azure.WebJobs.Script.Extensibility;
using Microsoft.Azure.WebJobs.Script.ExtensionBundle;
using Microsoft.Azure.WebJobs.Script.Workers;
using Microsoft.Azure.WebJobs.Script.Workers.Http;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using FunctionMetadata = Microsoft.Azure.WebJobs.Script.Description.FunctionMetadata;

namespace Microsoft.Azure.WebJobs.Script
{
    public class ScriptHost : JobHost, IScriptJobHost
    {
        internal const int DebugModeTimeoutMinutes = 15;
        private const string HostAssemblyName = "ScriptHost";
        private const string GeneratedTypeNamespace = "Host";
        internal const string GeneratedTypeName = "Functions";
        private readonly IApplicationLifetime _applicationLifetime;
        private readonly IScriptHostManager _scriptHostManager;
        private readonly IDistributedLockManager _distributedLockManager;
        private readonly IFunctionMetadataManager _functionMetadataManager;
        private readonly IFileLoggingStatusManager _fileLoggingStatusManager;
        private readonly IHostIdProvider _hostIdProvider;
        private readonly IHttpRoutesManager _httpRoutesManager;
        private readonly IMetricsLogger _metricsLogger = null;
        private readonly string _hostLogPath;
        private readonly IOptions<JobHostOptions> _hostOptions;
        private readonly bool _isHttpWorker;
        private readonly HttpWorkerOptions _httpWorkerOptions;
        private readonly IConfiguration _configuration;
        private readonly ScriptTypeLocator _typeLocator;
        private readonly IDebugStateProvider _debugManager;
        private readonly ICollection<IScriptBindingProvider> _bindingProviders;
        private readonly IJobHostMetadataProvider _metadataProvider;
        private readonly List<FunctionDescriptorProvider> _descriptorProviders = new List<FunctionDescriptorProvider>();
        private readonly ILoggerFactory _loggerFactory = null;
        private readonly string _instanceId;
        private readonly IEnvironment _environment;
        private readonly IFunctionDataCache _functionDataCache;
        private readonly IOptions<LanguageWorkerOptions> _languageWorkerOptions;
        private static readonly int _processId = Process.GetCurrentProcess().Id;

        private ValueStopwatch _stopwatch;
        private IPrimaryHostStateProvider _primaryHostStateProvider;
        public static readonly string Version = GetAssemblyFileVersion(typeof(ScriptHost).Assembly);
        private ScriptSettingsManager _settingsManager;
        private ILogger _logger = null;
        private string _workerRuntime;
        private IList<IDisposable> _eventSubscriptions = new List<IDisposable>();
        private IFunctionInvocationDispatcher _functionDispatcher;

        // Specify the "builtin binding types". These are types that are directly accesible without needing an explicit load gesture.
        // This is the set of bindings we shipped prior to binding extensibility.
        // Map from BindingType to the Assembly Qualified Type name for its IExtensionConfigProvider object.

        public ScriptHost(IOptions<JobHostOptions> options,
            IOptions<HttpWorkerOptions> httpWorkerOptions,
            IEnvironment environment,
            IJobHostContextFactory jobHostContextFactory,
            IConfiguration configuration,
            IDistributedLockManager distributedLockManager,
            IScriptEventManager eventManager,
            ILoggerFactory loggerFactory,
            IFunctionInvocationDispatcherFactory functionDispatcherFactory,
            IFunctionMetadataManager functionMetadataManager,
            IFileLoggingStatusManager fileLoggingStatusManager,
            IMetricsLogger metricsLogger,
            IOptions<ScriptJobHostOptions> scriptHostOptions,
            ITypeLocator typeLocator,
            IScriptHostManager scriptHostManager,
            IDebugStateProvider debugManager,
            IEnumerable<IScriptBindingProvider> bindingProviders,
            IPrimaryHostStateProvider primaryHostStateProvider,
            IJobHostMetadataProvider metadataProvider,
            IHostIdProvider hostIdProvider,
            IHttpRoutesManager httpRoutesManager,
            IApplicationLifetime applicationLifetime,
            IExtensionBundleManager extensionBundleManager,
            IFunctionDataCache functionDataCache,
            IOptions<LanguageWorkerOptions> languageWorkerOptions,
            ScriptSettingsManager settingsManager = null)
            : base(options, jobHostContextFactory)
        {
            _environment = environment;
            _typeLocator = typeLocator as ScriptTypeLocator
                ?? throw new ArgumentException(nameof(typeLocator), $"A {nameof(ScriptTypeLocator)} instance is required.");

            _instanceId = Guid.NewGuid().ToString();
            _hostOptions = options;
            _configuration = configuration;
            _distributedLockManager = distributedLockManager;
            _functionMetadataManager = functionMetadataManager;
            _fileLoggingStatusManager = fileLoggingStatusManager;
            _applicationLifetime = applicationLifetime;
            _hostIdProvider = hostIdProvider;
            _httpRoutesManager = httpRoutesManager;
            _isHttpWorker = httpWorkerOptions.Value.Description != null;
            _httpWorkerOptions = httpWorkerOptions.Value;
            ScriptOptions = scriptHostOptions.Value;
            _scriptHostManager = scriptHostManager;
            FunctionErrors = new Dictionary<string, ICollection<string>>(StringComparer.OrdinalIgnoreCase);
            EventManager = eventManager;
            _functionDispatcher = functionDispatcherFactory.GetFunctionDispatcher();
            _settingsManager = settingsManager ?? ScriptSettingsManager.Instance;
            ExtensionBundleManager = extensionBundleManager;

            _metricsLogger = metricsLogger;

            _hostLogPath = Path.Combine(ScriptOptions.RootLogPath, "Host");

            _workerRuntime = _environment.GetEnvironmentVariable(RpcWorkerConstants.FunctionWorkerRuntimeSettingName);
            _languageWorkerOptions = languageWorkerOptions;

            _loggerFactory = loggerFactory;
            _logger = loggerFactory.CreateLogger(LogCategories.Startup);
            Logger = _logger;

            _debugManager = debugManager;
            _primaryHostStateProvider = primaryHostStateProvider;
            _bindingProviders = new List<IScriptBindingProvider>(bindingProviders);
            _metadataProvider = metadataProvider;
            _eventSubscriptions.Add(EventManager.OfType<FunctionIndexingEvent>()
                .Subscribe(evt =>
                {
                    HandleHostError(evt.Exception);
                }));

            _functionDataCache = functionDataCache;
        }

        public event EventHandler HostInitializing;

        public event EventHandler HostInitialized;

        public event EventHandler HostStarted;

        public event EventHandler IsPrimaryChanged;

        public string InstanceId => ScriptOptions.InstanceId;

        public IScriptEventManager EventManager { get; }

        internal IExtensionBundleManager ExtensionBundleManager { get; }

        public ILogger Logger { get; internal set; }

        public ScriptJobHostOptions ScriptOptions { get; private set; }

        public static bool IsFunctionDataCacheEnabled { get; set; }

        /// <summary>
        /// Gets the collection of all valid Functions. For functions that are in error
        /// and were unable to load successfully, consult the <see cref="FunctionErrors"/> collection.
        /// </summary>
        public virtual ICollection<FunctionDescriptor> Functions { get; private set; } = new Collection<FunctionDescriptor>();

        // Maps from FunctionName to a set of errors for that function.
        public virtual IDictionary<string, ICollection<string>> FunctionErrors { get; private set; }

        public virtual bool IsPrimary => _primaryHostStateProvider.IsPrimary;

        public bool IsStandbyHost => ScriptOptions.IsStandbyConfiguration;

        public ScriptSettingsManager SettingsManager
        {
            get
            {
                return _settingsManager;
            }

            private set
            {
                _settingsManager = value;
            }
        }

        /// <summary>
        /// Gets a value indicating whether the host is in debug mode.
        /// </summary>
        public virtual bool InDebugMode => _debugManager.InDebugMode;

        /// <summary>
        /// Gets a value indicating whether the host is in diagnostic mode.
        /// </summary>
        public virtual bool InDiagnosticMode => _debugManager.InDiagnosticMode;

        internal IFunctionInvocationDispatcher FunctionDispatcher => _functionDispatcher;

        /// <summary>
        /// Returns true if the specified name is the name of a known function,
        /// regardless of whether the function is in error.
        /// </summary>
        /// <param name="name">The name of the function to check for.</param>
        /// <returns>True if the name matches a function; otherwise, false.</returns>
        public bool IsFunction(string name)
        {
            if (!string.IsNullOrEmpty(name) &&
                (Functions.Any(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase)) ||
                FunctionErrors.ContainsKey(name)))
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// Lookup a function by name
        /// </summary>
        /// <param name="name">name of function</param>
        /// <returns>function or null if not found</returns>
        public FunctionDescriptor GetFunctionOrNull(string name)
        {
            return Functions.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
        }

        public virtual async Task CallAsync(string method, Dictionary<string, object> arguments, CancellationToken cancellationToken = default(CancellationToken))
        {
            await base.CallAsync(method, arguments, cancellationToken);
        }

        protected override async Task StartAsyncCore(CancellationToken cancellationToken)
        {
            // Throw if cancellation occurred before initialization.
            cancellationToken.ThrowIfCancellationRequested();

            _ = LogInitializationAsync();

            await InitializeAsync(cancellationToken);

            // Throw if cancellation occurred during initialization.
            cancellationToken.ThrowIfCancellationRequested();

            await base.StartAsyncCore(cancellationToken);

            LogHostFunctionErrors();
        }

        /// <summary>
        /// Performs all required initialization on the host.
        /// Must be called before the host is started.
        /// </summary>
        public async Task InitializeAsync(CancellationToken cancellationToken = default)
        {
            _stopwatch = ValueStopwatch.StartNew();
            using (_metricsLogger.LatencyEvent(MetricEventNames.HostStartupLatency))
            {
                PreInitialize();
                HostInitializing?.Invoke(this, EventArgs.Empty);

                _workerRuntime = _workerRuntime ?? _environment.GetEnvironmentVariable(EnvironmentSettingNames.FunctionWorkerRuntime);

                // get worker config information and check to see if worker should index or not
                var workerConfigs = _languageWorkerOptions.Value.WorkerConfigs;

                bool workerIndexing = Utility.CanWorkerIndex(workerConfigs, _environment);

                // Generate Functions
                IEnumerable<FunctionMetadata> functionMetadataList = GetFunctionsMetadata(workerIndexing);

                if (!_environment.IsPlaceholderModeEnabled())
                {
                    string runtimeStack = _workerRuntime;

                    if (!string.IsNullOrEmpty(runtimeStack))
                    {
                        // Appending the runtime version is currently only enabled for linux consumption. This will be eventually enabled for
                        // Windows Consumption as well.
                        string runtimeVersion = _environment.GetEnvironmentVariable(RpcWorkerConstants.FunctionWorkerRuntimeVersionSettingName);

                        if (!string.IsNullOrEmpty(runtimeVersion))
                        {
                            runtimeStack = string.Concat(runtimeStack, "-", runtimeVersion);
                        }
                    }

                    _metricsLogger.LogEvent(string.Format(MetricEventNames.HostStartupRuntimeLanguage, Sanitizer.Sanitize(runtimeStack)));

                    Utility.LogAutorestGeneratedJsonIfExists(ScriptOptions.RootScriptPath, _logger);
                }

                IsFunctionDataCacheEnabled = GetIsFunctionDataCacheEnabled();

                await InitializeFunctionDescriptorsAsync(functionMetadataList, cancellationToken);

                if (!workerIndexing)
                {
                    // Initialize worker function invocation dispatcher only for valid functions after creating function descriptors
                    // Dispatcher not needed for codeless function.
                    // Disptacher needed for non-dotnet codeless functions
                    var filteredFunctionMetadata = functionMetadataList.Where(m => !Utility.IsCodelessDotNetLanguageFunction(m));
                    await _functionDispatcher.InitializeAsync(Utility.GetValidFunctions(filteredFunctionMetadata, Functions), cancellationToken);
                }

                GenerateFunctions();
                ScheduleFileSystemCleanup();
            }
        }

        /// <summary>
        /// Checks if the conditions to use <see cref="IFunctionDataCache"/> are met (if a valid implementation was found at runtime,
        /// if the setting was enabled, the app is using out-of-proc languages which communicate with the host over shared memory).
        /// </summary>
        /// <returns><see cref="true"/> if <see cref="IFunctionDataCache"/> can be used, <see cref="false"/> otherwise.</returns>
        private bool GetIsFunctionDataCacheEnabled()
        {
            if (Utility.IsDotNetLanguageFunction(_workerRuntime) ||
                ContainsDotNetFunctionDescriptorProvider() ||
                _functionDataCache == null)
            {
                return false;
            }

            return _functionDataCache.IsEnabled;
        }

        private async Task LogInitializationAsync()
        {
            // If the host id is explicitly set, emit a warning that this could cause issues and shouldn't be done
            if (_configuration[ConfigurationSectionNames.HostIdPath] != null)
            {
                _logger.HostIdIsSet();
            }

            string extensionVersion = _environment.GetEnvironmentVariable(EnvironmentSettingNames.FunctionsExtensionVersion);
            string hostId = await _hostIdProvider.GetHostIdAsync(CancellationToken.None);
            _logger.StartingHost(hostId, InstanceId, Version, _processId, AppDomain.CurrentDomain.Id, InDebugMode, InDiagnosticMode, extensionVersion);
        }

        private void LogHostFunctionErrors()
        {
            // Split these into individual logs so we can include the functionName.
            foreach (var error in FunctionErrors)
            {
                string functionErrors = string.Join(Environment.NewLine, error.Value);
                _logger.FunctionError(error.Key, functionErrors);
            }
        }

        /// <summary>
        /// Gets metadata collection of functions configured.
        /// </summary>
        /// <returns>A metadata collection of functions and proxies configured.</returns>
        private IEnumerable<FunctionMetadata> GetFunctionsMetadata(bool workerIndexing)
        {
            IEnumerable<FunctionMetadata> functionMetadata;
            if (workerIndexing)
            {
                _logger.LogInformation("Worker indexing is enabled");
                functionMetadata = _functionMetadataManager.GetFunctionMetadata(forceRefresh: false, dispatcher: _functionDispatcher);
            }
            else
            {
                functionMetadata = _functionMetadataManager.GetFunctionMetadata(false);
                _workerRuntime = _workerRuntime ?? Utility.GetWorkerRuntime(functionMetadata);
            }
            foreach (var error in _functionMetadataManager.Errors)
            {
                FunctionErrors.Add(error.Key, error.Value.ToArray());
            }

            return functionMetadata;
        }

        internal static Collection<CustomAttributeBuilder> CreateTypeAttributes(ScriptJobHostOptions scriptConfig)
        {
            Collection<CustomAttributeBuilder> customAttributes = new Collection<CustomAttributeBuilder>();

            // apply the timeout settings to our type
            if (scriptConfig.FunctionTimeout != null)
            {
                var timeoutBuilder = CustomAttributeBuilderUtility.GetTimeoutCustomAttributeBuilder(scriptConfig.FunctionTimeout.Value);
                customAttributes.Add(timeoutBuilder);
            }
            // apply retry settings for function execution
            if (scriptConfig.Retry != null)
            {
                // apply the retry settings from host.json
                var retryCustomAttributeBuilder = CustomAttributeBuilderUtility.GetRetryCustomAttributeBuilder(scriptConfig.Retry);
                if (retryCustomAttributeBuilder != null)
                {
                    customAttributes.Add(retryCustomAttributeBuilder);
                }
            }

            return customAttributes;
        }

        // TODO: DI (FACAVAL) Remove this method.
        // all restart/shutdown requests should go through the
        internal Task RestartAsync()
        {
            _scriptHostManager.RestartHostAsync();
            return Task.CompletedTask;
        }

        internal void Shutdown()
        {
            _applicationLifetime.StopApplication();
        }

        private void OnHostLeaseChanged(object sender, EventArgs e)
        {
            IsPrimaryChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// Perform any early initialization operations.
        /// </summary>
        private void PreInitialize()
        {
            // Validate extension configuration
            if (_environment.IsRunningAsHostedSiteExtension())
            {
                string extensionVersion = _environment.GetEnvironmentVariable(EnvironmentSettingNames.FunctionsExtensionVersion);

                if (string.IsNullOrEmpty(extensionVersion))
                {
                    throw new HostInitializationException($"Invalid site extension configuration. " +
                        $"Please update the App Setting '{EnvironmentSettingNames.FunctionsExtensionVersion}' to a valid value (e.g. ~2). " +
                        $"The value cannot be missing or an empty string.");
                }
                else if (string.Equals(extensionVersion, "latest", StringComparison.OrdinalIgnoreCase))
                {
                    _logger.VersionRecommendation(extensionVersion);
                }
            }

            // Log whether App Insights is enabled
            if (!string.IsNullOrEmpty(_settingsManager.ApplicationInsightsInstrumentationKey) || !string.IsNullOrEmpty(_settingsManager.ApplicationInsightsConnectionString))
            {
                _metricsLogger.LogEvent(MetricEventNames.ApplicationInsightsEnabled);
            }
            else
            {
                _metricsLogger.LogEvent(MetricEventNames.ApplicationInsightsDisabled);
            }

            InitializeFileSystem();
        }

        /// <summary>
        /// Set up any required directories or files.
        /// </summary>
        private void InitializeFileSystem()
        {
            if (_fileLoggingStatusManager.IsFileLoggingEnabled)
            {
                FileUtility.EnsureDirectoryExists(_hostLogPath);
            }

            if (!ScriptOptions.IsFileSystemReadOnly)
            {
                FileUtility.EnsureDirectoryExists(ScriptOptions.RootScriptPath);
            }
        }

        /// <summary>
        /// Generate function wrappers from descriptors.
        /// </summary>
        private void GenerateFunctions()
        {
            // generate Type level attributes
            var typeAttributes = CreateTypeAttributes(ScriptOptions);

            string generatingMsg = string.Format(CultureInfo.InvariantCulture, "Generating {0} job function(s)", Functions.Count);
            _logger?.LogInformation(generatingMsg);

            // generate the Type wrapper
            string typeName = string.Format(CultureInfo.InvariantCulture, "{0}.{1}", GeneratedTypeNamespace, GeneratedTypeName);
            Type functionWrapperType = FunctionGenerator.Generate(HostAssemblyName, typeName, typeAttributes, Functions);

            // configure the Type locator
            var types = new List<Type>
            {
                functionWrapperType
            };

            foreach (var descriptor in Functions)
            {
                if (descriptor.Metadata.Properties.TryGetValue(ScriptConstants.FunctionMetadataDirectTypeKey, out Type type))
                {
                    types.Add(type);
                }
            }

            _typeLocator.SetTypes(types);
        }

        /// <summary>
        /// Initialize function descriptors from metadata.
        /// </summary>
        internal async Task InitializeFunctionDescriptorsAsync(IEnumerable<FunctionMetadata> functionMetadata, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            AddFunctionDescriptors(functionMetadata);

            Collection<FunctionDescriptor> functions;
            using (_metricsLogger.LatencyEvent(MetricEventNames.HostStartupGetFunctionDescriptorsLatency))
            {
                _logger.CreatingDescriptors();
                functions = await GetFunctionDescriptorsAsync(functionMetadata, _descriptorProviders, cancellationToken);
                _logger.DescriptorsCreated();
            }
            Functions = functions;
        }

        private void AddFunctionDescriptors(IEnumerable<FunctionMetadata> functionMetadata)
        {
            if (_environment.IsPlaceholderModeEnabled())
            {
                _logger.HostIsInPlaceholderMode();
                _logger.AddingDescriptorProviderForLanguage(RpcWorkerConstants.DotNetLanguageWorkerName);
                _descriptorProviders.Add(new DotNetFunctionDescriptorProvider(this, ScriptOptions, _bindingProviders, _metricsLogger, _loggerFactory));
            }
            else if (_isHttpWorker)
            {
                _logger.AddingDescriptorProviderForHttpWorker();
                _descriptorProviders.Add(new HttpFunctionDescriptorProvider(this, ScriptOptions, _bindingProviders, _functionDispatcher, _loggerFactory, _applicationLifetime, _httpWorkerOptions.InitializationTimeout));
            }
            else if (string.Equals(_workerRuntime, RpcWorkerConstants.DotNetLanguageWorkerName, StringComparison.OrdinalIgnoreCase))
            {
                _logger.AddingDescriptorProviderForLanguage(RpcWorkerConstants.DotNetLanguageWorkerName);
                _descriptorProviders.Add(new DotNetFunctionDescriptorProvider(this, ScriptOptions, _bindingProviders, _metricsLogger, _loggerFactory));
            }
            else
            {
                _logger.AddingDescriptorProviderForLanguage(_workerRuntime);

                var workerConfig = _languageWorkerOptions.Value.WorkerConfigs?.FirstOrDefault(c => c.Description.Language.Equals(_workerRuntime, StringComparison.OrdinalIgnoreCase));

                // If there's no worker config, use the default (for legacy behavior; mostly for tests).
                TimeSpan initializationTimeout = workerConfig?.CountOptions?.InitializationTimeout ?? WorkerProcessCountOptions.DefaultInitializationTimeout;

                _descriptorProviders.Add(new RpcFunctionDescriptorProvider(this, _workerRuntime, ScriptOptions, _bindingProviders,
                    _functionDispatcher, _loggerFactory, _applicationLifetime, initializationTimeout));
            }

            // Codeless functions run side by side with regular functions.
            // In addition to descriptors already added here, we need to ensure all codeless functions
            // also have associated descriptors.
            AddCodelessDescriptors(functionMetadata);
        }

        /// <summary>
        /// Checks if the list of descriptors contains any <see cref="DotNetFunctionDescriptorProvider"/>.
        /// </summary>
        /// <returns><see cref="true"/> if <see cref="DotNetFunctionDescriptorProvider"/> found, <see cref="false"/> otherwise.</returns>
        private bool ContainsDotNetFunctionDescriptorProvider()
        {
            return _descriptorProviders.Any(d => d is DotNetFunctionDescriptorProvider);
        }

        /// <summary>
        /// Adds a DotNetFunctionDescriptorProvider to the list of descriptors if any function metadata has language set to "codeless" in it.
        /// </summary>
        private void AddCodelessDescriptors(IEnumerable<FunctionMetadata> functionMetadata)
        {
            // If we have a codeless function, we need to add a .NET descriptor provider. But only if it wasn't already added.
            // At the moment, we are assuming that all codeless functions will have language as DotNetAssembly
            if (!_descriptorProviders.Any(d => d is DotNetFunctionDescriptorProvider)
                && functionMetadata.Any(m => m.IsCodeless()))
            {
                _descriptorProviders.Add(new DotNetFunctionDescriptorProvider(this, ScriptOptions, _bindingProviders, _metricsLogger, _loggerFactory));
            }
        }

        /// <summary>
        /// Clean up any old files or directories.
        /// </summary>
        private void ScheduleFileSystemCleanup()
        {
            Utility.ExecuteAfterColdStartDelay(_environment, () =>
            {
                if (ScriptOptions.FileLoggingMode != FileLoggingMode.Never)
                {
                    PurgeOldLogDirectories();
                }
            });
        }

        /// <summary>
        /// Iterate through all function log directories and remove any that don't
        /// correspond to a function.
        /// </summary>
        private void PurgeOldLogDirectories()
        {
            try
            {
                if (!Directory.Exists(this.ScriptOptions.RootScriptPath))
                {
                    return;
                }

                // Create a lookup of all potential functions (whether they're valid or not)
                // It is important that we determine functions based on the presence of a folder,
                // not whether we've identified a valid function from that folder. This ensures
                // that we don't delete logs/secrets for functions that transition into/out of
                // invalid unparsable states.
                var functionLookup = Directory.EnumerateDirectories(ScriptOptions.RootScriptPath).ToLookup(p => Path.GetFileName(p), StringComparer.OrdinalIgnoreCase);

                string rootLogFilePath = Path.Combine(ScriptOptions.RootLogPath, "Function");
                if (!Directory.Exists(rootLogFilePath))
                {
                    return;
                }

                var logFileDirectory = new DirectoryInfo(rootLogFilePath);
                foreach (var logDir in logFileDirectory.GetDirectories())
                {
                    if (!functionLookup.Contains(logDir.Name))
                    {
                        // the directory no longer maps to a running function
                        // so delete it
                        try
                        {
                            // destructive operation, thus log
                            _logger.DeletingLogDirectory(logDir.FullName);
                            logDir.Delete(recursive: true);
                        }
                        catch
                        {
                            // Purge is best effort
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // Purge is best effort
                _logger.ErrorPurgingLogFiles(ex);
            }
        }

        // Validate that for any precompiled assembly, all functions have the same configuration precedence.
        private void VerifyPrecompileStatus(IEnumerable<FunctionDescriptor> functions)
        {
            HashSet<string> illegalScriptAssemblies = new HashSet<string>();

            Dictionary<string, bool> mapAssemblySettings = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            foreach (var function in functions)
            {
                var metadata = function.Metadata;
                var scriptFile = metadata.ScriptFile;
                if (scriptFile != null && scriptFile.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                {
                    bool isDirect = metadata.IsDirect();
                    if (mapAssemblySettings.TryGetValue(scriptFile, out bool prevIsDirect))
                    {
                        if (prevIsDirect != isDirect)
                        {
                            illegalScriptAssemblies.Add(scriptFile);
                        }
                    }
                    mapAssemblySettings[scriptFile] = isDirect;
                }
            }

            foreach (var function in functions)
            {
                var metadata = function.Metadata;
                var scriptFile = metadata.ScriptFile;

                if (illegalScriptAssemblies.Contains(scriptFile))
                {
                    // Error. All entries pointing to the same dll must have the same value for IsDirect
                    string msg = string.Format(CultureInfo.InvariantCulture, "Configuration error: all functions in {0} must have the same value for 'configurationSource'.",
                        scriptFile);

                    // Adding a function error will cause this function to get ignored
                    Utility.AddFunctionError(this.FunctionErrors, metadata.Name, msg);

                    _logger.ConfigurationError(msg);
                }

                return;
            }
        }

        /// <summary>
        /// Sets the type that should be directly loaded by WebJobs if using attribute based configuration (these have the "configurationSource" : "attributes" set)
        /// They will be indexed and invoked directly by the WebJobs SDK and skip the IL generator and invoker paths.
        /// </summary>
        private void TrySetDirectType(FunctionMetadata metadata)
        {
            if (!metadata.IsDirect())
            {
                return;
            }

            string path = metadata.ScriptFile;
            var typeName = Utility.GetFullClassName(metadata.EntryPoint);

            Assembly assembly = FunctionAssemblyLoadContext.Shared.LoadFromAssemblyPath(path);
            var type = assembly.GetType(typeName);
            if (type != null)
            {
                metadata.Properties.Add(ScriptConstants.FunctionMetadataDirectTypeKey, type);
            }
            else
            {
                // This likely means the function.json and dlls are out of sync. Perhaps a badly generated function.json?
                _logger.FailedToLoadType(typeName, path);
            }
        }

        internal async Task<Collection<FunctionDescriptor>> GetFunctionDescriptorsAsync(IEnumerable<FunctionMetadata> functions, IEnumerable<FunctionDescriptorProvider> descriptorProviders, CancellationToken cancellationToken)
        {
            Collection<FunctionDescriptor> functionDescriptors = new Collection<FunctionDescriptor>();
            if (!cancellationToken.IsCancellationRequested)
            {
                var httpFunctions = new Dictionary<string, HttpTriggerAttribute>();

                Utility.VerifyFunctionsMatchSpecifiedLanguage(functions, _workerRuntime, _environment.IsPlaceholderModeEnabled(), _isHttpWorker, cancellationToken);

                foreach (FunctionMetadata metadata in functions)
                {
                    try
                    {
                        bool created = false;
                        FunctionDescriptor descriptor = null;
                        foreach (var provider in descriptorProviders)
                        {
                            (created, descriptor) = await provider.TryCreate(metadata);
                            if (created)
                            {
                                break;
                            }
                        }

                        if (descriptor != null)
                        {
                            ValidateFunction(descriptor, httpFunctions);
                            functionDescriptors.Add(descriptor);
                        }

                        // If this is metadata represents a function that supports direct type indexing,
                        // set that type int he function metadata
                        TrySetDirectType(metadata);
                    }
                    catch (Exception ex)
                    {
                        // log any unhandled exceptions and continue
                        Utility.AddFunctionError(FunctionErrors, metadata.Name, Utility.FlattenException(ex, includeSource: false));
                    }
                }

                VerifyPrecompileStatus(functionDescriptors);
            }
            return functionDescriptors;
        }

        internal static void ValidateFunction(FunctionDescriptor function, Dictionary<string, HttpTriggerAttribute> httpFunctions)
        {
            var httpTrigger = function.HttpTriggerAttribute;
            if (httpTrigger != null)
            {
                ValidateHttpFunction(function.Name, httpTrigger);

                // prevent duplicate/conflicting routes for functions
                foreach (var pair in httpFunctions)
                {
                    if (HttpRoutesConflict(httpTrigger, pair.Value))
                    {
                        throw new InvalidOperationException($"The route specified conflicts with the route defined by function '{pair.Key}'.");
                    }
                }

                if (httpFunctions.ContainsKey(function.Name))
                {
                    throw new InvalidOperationException($"The function name '{function.Name}' must be unique within the function app.");
                }

                httpFunctions.Add(function.Name, httpTrigger);
            }
        }

        internal static void ValidateHttpFunction(string functionName, HttpTriggerAttribute httpTrigger)
        {
            if (string.IsNullOrWhiteSpace(httpTrigger.Route))
            {
                // if no explicit route is provided, default to the function name
                httpTrigger.Route = functionName;
            }

            // disallow custom routes in our own reserved route space
            string httpRoute = httpTrigger.Route.Trim('/').ToLowerInvariant();
            if (httpRoute.StartsWith("admin") ||
                httpRoute.StartsWith("runtime"))
            {
                throw new InvalidOperationException("The specified route conflicts with one or more built in routes.");
            }
        }

        // A route is in conflict if the route matches any other existing
        // route and there is intersection in the http methods of the two functions
        internal static bool HttpRoutesConflict(HttpTriggerAttribute httpTrigger, HttpTriggerAttribute otherHttpTrigger)
        {
            if (string.Compare(httpTrigger.Route.Trim('/'), otherHttpTrigger.Route.Trim('/'), StringComparison.OrdinalIgnoreCase) != 0)
            {
                // routes differ, so no conflict
                return false;
            }

            if (httpTrigger.Methods == null || httpTrigger.Methods.Length == 0 ||
                otherHttpTrigger.Methods == null || otherHttpTrigger.Methods.Length == 0)
            {
                // if either methods collection is null or empty that means
                // "all methods", which will intersect with any method collection
                return true;
            }

            return httpTrigger.Methods.Intersect(otherHttpTrigger.Methods).Any();
        }

        private void HandleHostError(Exception exception)
        {
            if (exception == null)
            {
                throw new ArgumentNullException(nameof(exception));
            }

            // Note: We do not log to ILogger here as any error has already been logged.

            if (exception is FunctionInvocationException)
            {
                // For all function invocation errors, we notify the invoker so it can
                // log the error as needed to its function specific logs.
                FunctionInvocationException invocationException = exception as FunctionInvocationException;
                NotifyInvoker(invocationException.MethodName, invocationException);
            }
            else if (exception is FunctionIndexingException || exception is FunctionListenerException)
            {
                // For all startup time indexing/listener errors, we accumulate them per function
                FunctionException functionException = exception as FunctionException;
                string formattedError = Utility.FlattenException(functionException);
                Utility.AddFunctionError(FunctionErrors, functionException.MethodName, formattedError);

                // Also notify the invoker so the error can also be written to the function
                // log file
                NotifyInvoker(functionException.MethodName, functionException);
            }
            else
            {
                // See if we can identify which function caused the error, and if we can
                // log the error as needed to its function specific logs.
                if (TryGetFunctionFromException(Functions, exception, out FunctionDescriptor function))
                {
                    NotifyInvoker(function.Name, exception);
                }
            }
        }

        internal static bool TryGetFunctionFromException(ICollection<FunctionDescriptor> functions, Exception exception, out FunctionDescriptor function)
        {
            function = null;
            if (functions == null)
            {
                return false;
            }
            string errorStack = exception.ToString().ToLowerInvariant();
            foreach (var currFunction in functions)
            {
                // For each function, we search the entire error stack trace to see if it contains
                // the function entry/primary script path. If it does, we're virtually certain that
                // that function caused the error (e.g. as in the case of global unhandled exceptions
                // coming from Node.js scripts).
                // We use the directory name for the script rather than the full script path itself to ensure
                // that we handle cases where the error might be coming from some other script (e.g. an NPM
                // module) that is part of the function.
                string absoluteScriptPath = Path.GetFullPath(currFunction.Metadata.ScriptFile).ToLowerInvariant();
                string functionDirectory = Path.GetDirectoryName(absoluteScriptPath);
                if (errorStack.Contains(functionDirectory))
                {
                    function = currFunction;
                    return true;
                }
            }

            return false;
        }

        private void NotifyInvoker(string methodName, Exception ex)
        {
            var functionDescriptor = this.Functions.SingleOrDefault(p =>
                    string.Compare(Utility.GetFunctionShortName(methodName), p.Name, StringComparison.OrdinalIgnoreCase) == 0 ||
                    string.Compare(p.Metadata.EntryPoint, methodName, StringComparison.OrdinalIgnoreCase) == 0);
            if (functionDescriptor != null)
            {
                functionDescriptor.Invoker.OnError(ex);
            }
        }

        private void ApplyJobHostMetadata()
        {
            // TODO: DI (FACAVAL) Review
            foreach (var function in Functions)
            {
                var metadata = _metadataProvider.GetFunctionMetadata(function.Metadata.Name);
                if (metadata != null)
                {
                    function.Metadata.SetIsDisabled(metadata.IsDisabled);
                }
            }
        }

        internal static string GetAssemblyFileVersion(Assembly assembly)
        {
            AssemblyFileVersionAttribute fileVersionAttr = assembly.GetCustomAttribute<AssemblyFileVersionAttribute>();
            return fileVersionAttr?.Version ?? "Unknown";
        }

        protected override void OnHostInitialized()
        {
            ApplyJobHostMetadata();

            _httpRoutesManager.InitializeHttpFunctionRoutes(this);

            _logger.ScriptHostInitialized((long)_stopwatch.GetElapsedTime().TotalMilliseconds);

            HostInitialized?.Invoke(this, EventArgs.Empty);

            base.OnHostInitialized();
        }

        protected override void OnHostStarted()
        {
            HostStarted?.Invoke(this, EventArgs.Empty);

            base.OnHostStarted();

            _logger.ScriptHostStarted((long)_stopwatch.GetElapsedTime().TotalMilliseconds);
        }

        protected override async Task StopAsyncCore(CancellationToken cancellationToken)
        {
            _logger.StoppingScriptHost(ScriptOptions.InstanceId);
            await base.StopAsyncCore(cancellationToken);
            _logger.StoppedScriptHost(ScriptOptions.InstanceId);
        }

        protected override void Dispose(bool disposing)
        {
            _logger.DisposingScriptHost(ScriptOptions.InstanceId);

            if (disposing)
            {
                foreach (var subscription in _eventSubscriptions)
                {
                    subscription.Dispose();
                }

                _functionDispatcher?.Dispose();

                foreach (var function in Functions)
                {
                    (function.Invoker as IDisposable)?.Dispose();
                }

                if (_descriptorProviders != null)
                {
                    foreach (var provider in _descriptorProviders)
                    {
                        (provider as IDisposable)?.Dispose();
                    }
                }
            }

            // dispose base last to ensure that errors there don't
            // cause us to not dispose ourselves
            base.Dispose(disposing);

            _logger.DisposedScriptHost(ScriptOptions.InstanceId);
        }
    }
}