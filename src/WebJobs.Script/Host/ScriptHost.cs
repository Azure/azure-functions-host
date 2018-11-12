// Copyright (c) .NET Foundation. All rights reserved.
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
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Host.Executors;
using Microsoft.Azure.WebJobs.Host.Indexers;
using Microsoft.Azure.WebJobs.Host.Listeners;
using Microsoft.Azure.WebJobs.Logging;
using Microsoft.Azure.WebJobs.Script.Abstractions;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Configuration;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.Eventing;
using Microsoft.Azure.WebJobs.Script.Extensibility;
using Microsoft.Azure.WebJobs.Script.Grpc;
using Microsoft.Azure.WebJobs.Script.Rpc;
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
        private readonly IScriptJobHostEnvironment _scriptHostEnvironment;
        private readonly string _storageConnectionString;
        private readonly IDistributedLockManager _distributedLockManager;
        private readonly IFunctionMetadataManager _functionMetadataManager;
        private readonly IHostIdProvider _hostIdProvider;
        private readonly IProxyMetadataManager _proxyMetadataManager;
        private readonly IEnumerable<WorkerConfig> _workerConfigs;
        private readonly IMetricsLogger _metricsLogger = null;
        private readonly string _hostLogPath;
        private readonly Stopwatch _stopwatch = new Stopwatch();
        private readonly string _currentRuntimelanguage;
        private readonly IOptions<JobHostOptions> _hostOptions;
        private readonly IConfiguration _configuration;
        private readonly ScriptTypeLocator _typeLocator;
        private readonly IDebugStateProvider _debugManager;
        private readonly ICollection<IScriptBindingProvider> _bindingProviders;
        private readonly IJobHostMetadataProvider _metadataProvider;
        private readonly List<FunctionDescriptorProvider> _descriptorProviders = new List<FunctionDescriptorProvider>();
        private readonly ILoggerFactory _loggerFactory = null;
        private readonly string _instanceId;
        private readonly IEnvironment _environment;

        private IPrimaryHostStateProvider _primaryHostStateProvider;
        public static readonly string Version = GetAssemblyFileVersion(typeof(ScriptHost).Assembly);
        private ScriptSettingsManager _settingsManager;
        private ILogger _logger = null;

        private IRpcServer _rpcService;
        private IList<IDisposable> _eventSubscriptions = new List<IDisposable>();
        private IFunctionDispatcher _functionDispatcher;
        private IProcessRegistry _processRegistry = new EmptyProcessRegistry();
        private ILanguageWorkerChannel _languageWorkerChannel;
        private IOptionsMonitor<ScriptApplicationHostOptions> _scriptApplicationHostOptions;

        // Specify the "builtin binding types". These are types that are directly accesible without needing an explicit load gesture.
        // This is the set of bindings we shipped prior to binding extensibility.
        // Map from BindingType to the Assembly Qualified Type name for its IExtensionConfigProvider object.

        public ScriptHost(IOptions<JobHostOptions> options,
            IOptions<LanguageWorkerOptions> languageWorkerOptions,
            IEnvironment environment,
            IJobHostContextFactory jobHostContextFactory,
            IConfiguration configuration,
            IDistributedLockManager distributedLockManager,
            IScriptEventManager eventManager,
            ILanguageWorkerService languageWorkerService,
            ILoggerFactory loggerFactory,
            IFunctionMetadataManager functionMetadataManager,
            IProxyMetadataManager proxyMetadataManager,
            IMetricsLogger metricsLogger,
            IOptions<ScriptJobHostOptions> scriptHostOptions,
            IOptionsMonitor<ScriptApplicationHostOptions> applicationHostOptions,
            ITypeLocator typeLocator,
            IScriptJobHostEnvironment scriptHostEnvironment,
            IDebugStateProvider debugManager,
            IEnumerable<IScriptBindingProvider> bindingProviders,
            IPrimaryHostStateProvider primaryHostStateProvider,
            IJobHostMetadataProvider metadataProvider,
            IHostIdProvider hostIdProvider,
            ScriptSettingsManager settingsManager = null)
            : base(options, jobHostContextFactory)
        {
            _environment = environment;
            _typeLocator = typeLocator as ScriptTypeLocator
                ?? throw new ArgumentException(nameof(typeLocator), $"A {nameof(ScriptTypeLocator)} instance is required.");
            _instanceId = Guid.NewGuid().ToString();
            _hostOptions = options;
            _scriptApplicationHostOptions = applicationHostOptions;
            _configuration = configuration;
            _storageConnectionString = configuration.GetWebJobsConnectionString(ConnectionStringNames.Storage);
            _distributedLockManager = distributedLockManager;
            _functionMetadataManager = functionMetadataManager;
            _hostIdProvider = hostIdProvider;
            _proxyMetadataManager = proxyMetadataManager;
            _workerConfigs = languageWorkerOptions.Value.WorkerConfigs;

            ScriptOptions = scriptHostOptions.Value;
            _languageWorkerChannel = languageWorkerService.JavaWorkerChannel;
            _scriptHostEnvironment = scriptHostEnvironment;
            FunctionErrors = new Dictionary<string, ICollection<string>>(StringComparer.OrdinalIgnoreCase);

            EventManager = eventManager;

            _settingsManager = settingsManager ?? ScriptSettingsManager.Instance;

            _metricsLogger = metricsLogger;

            _hostLogPath = Path.Combine(ScriptOptions.RootLogPath, "Host");

            _currentRuntimelanguage = _environment.GetEnvironmentVariable(LanguageWorkerConstants.FunctionWorkerRuntimeSettingName);

            _loggerFactory = loggerFactory;
            _logger = loggerFactory.CreateLogger(LogCategories.Startup);
            Logger = _logger;

            _debugManager = debugManager;
            _primaryHostStateProvider = primaryHostStateProvider;
            _bindingProviders = new List<IScriptBindingProvider>(bindingProviders);
            _metadataProvider = metadataProvider;
        }

        public event EventHandler HostInitializing;

        public event EventHandler HostInitialized;

        public event EventHandler HostStarted;

        public event EventHandler IsPrimaryChanged;

        public string InstanceId => ScriptOptions.InstanceId;

        public IScriptEventManager EventManager { get; }

        public ILogger Logger { get; internal set; }

        public ScriptJobHostOptions ScriptOptions { get; private set; }

        /// <summary>
        /// Gets the collection of all valid Functions. For functions that are in error
        /// and were unable to load successfully, consult the <see cref="FunctionErrors"/> collection.
        /// </summary>
        public virtual ICollection<FunctionDescriptor> Functions { get; private set; } = new Collection<FunctionDescriptor>();

        // Maps from FunctionName to a set of errors for that function.
        public virtual IDictionary<string, ICollection<string>> FunctionErrors { get; private set; }

        public virtual bool IsPrimary
        {
            get
            {
                return _primaryHostStateProvider.IsPrimary;
            }
        }

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

        internal IFunctionDispatcher FunctionDispatcher => _functionDispatcher;

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

        internal static void AddLanguageWorkerChannelErrors(IFunctionDispatcher functionDispatcher, IDictionary<string, ICollection<string>> functionErrors)
        {
            foreach (KeyValuePair<WorkerConfig, LanguageWorkerState> kvp in functionDispatcher.LanguageWorkerChannelStates)
            {
                WorkerConfig workerConfig = kvp.Key;
                LanguageWorkerState workerState = kvp.Value;
                foreach (var functionRegistrationContext in workerState.GetRegistrations())
                {
                    var exMessage = $"Failed to start language worker process for: {workerConfig.Language}";
                    var languageWorkerChannelException = workerState.Errors != null && workerState.Errors.Count > 0 ? new LanguageWorkerChannelException(exMessage, workerState.Errors[workerState.Errors.Count - 1]) : new LanguageWorkerChannelException(exMessage);
                    Utility.AddFunctionError(functionErrors, functionRegistrationContext.Metadata.Name, Utility.FlattenException(languageWorkerChannelException, includeSource: false));
                }
            }
        }

        protected override async Task StartAsyncCore(CancellationToken cancellationToken)
        {
            var ignore = LogInitializationAsync();

            await InitializeAsync();
            await base.StartAsyncCore(cancellationToken);

            LogHostFunctionErrors();
        }

        /// <summary>
        /// Performs all required initialization on the host.
        /// Must be called before the host is started.
        /// </summary>
        public async Task InitializeAsync()
        {
            _stopwatch.Start();
            using (_metricsLogger.LatencyEvent(MetricEventNames.HostStartupLatency))
            {
                PreInitialize();
                HostInitializing?.Invoke(this, EventArgs.Empty);

                // Generate Functions
                IEnumerable<FunctionMetadata> functions = GetFunctionsMetadata();
                InitializeWorkersAsync();
                var directTypes = GetDirectTypes(functions);
                await InitializeFunctionDescriptorsAsync(functions);
                GenerateFunctions(directTypes);

                CleanupFileSystem();
            }
        }

        private async Task LogInitializationAsync()
        {
            // If the host id is explicitly set, emit a warning that this could cause issues and shouldn't be done
            if (_configuration[ConfigurationSectionNames.HostIdPath] != null)
            {
                _logger.LogWarning("Host id explicitly set in configuration. This is not a recommended configuration and may lead to unexpected behavior.");
            }

            string extensionVersion = _environment.GetEnvironmentVariable(EnvironmentSettingNames.FunctionsExtensionVersion);
            string hostId = await _hostIdProvider.GetHostIdAsync(CancellationToken.None);
            string message = $"Starting Host (HostId={hostId}, InstanceId={InstanceId}, Version={Version}, ProcessId={Process.GetCurrentProcess().Id}, AppDomainId={AppDomain.CurrentDomain.Id}, InDebugMode={InDebugMode}, InDiagnosticMode={InDiagnosticMode}, FunctionsExtensionVersion={extensionVersion})";
            _logger.LogInformation(message);
        }

        private void LogHostFunctionErrors()
        {
            if (FunctionErrors.Count > 0)
            {
                var builder = new StringBuilder();
                builder.AppendLine(string.Format(CultureInfo.InvariantCulture, "The following {0} functions are in error:", FunctionErrors.Count));
                foreach (var error in FunctionErrors)
                {
                    string functionErrors = string.Join(Environment.NewLine, error.Value);
                    builder.AppendLine(string.Format(CultureInfo.InvariantCulture, "{0}: {1}", error.Key, functionErrors));
                }
                string message = builder.ToString();
                _logger.LogError(message);
            }
        }

        /// <summary>
        /// Gets metadata collection of functions and proxies configured.
        /// </summary>
        /// <returns>A metadata collection of functions and proxies configured.</returns>
        private IEnumerable<FunctionMetadata> GetFunctionsMetadata()
        {
            IEnumerable<FunctionMetadata> functionMetadata = _functionMetadataManager.Functions;
            foreach (var error in _functionMetadataManager.Errors)
            {
                FunctionErrors.Add(error.Key, error.Value.ToArray());
            }

            // Get proxies metadata
            var proxyMetadata = _proxyMetadataManager.ProxyMetadata;
            if (!proxyMetadata.Functions.IsDefaultOrEmpty)
            {
                // Add the proxy descriptor provider
                _descriptorProviders.Add(new ProxyFunctionDescriptorProvider(this, ScriptOptions, _bindingProviders, proxyMetadata.ProxyClient, _loggerFactory));
                functionMetadata = proxyMetadata.Functions.Concat(functionMetadata);
            }

            foreach (var error in proxyMetadata.Errors)
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
                Type timeoutType = typeof(TimeoutAttribute);
                ConstructorInfo ctorInfo = timeoutType.GetConstructor(new[] { typeof(string) });

                PropertyInfo[] propertyInfos = new[]
                {
                    timeoutType.GetProperty("ThrowOnTimeout"),
                    timeoutType.GetProperty("TimeoutWhileDebugging")
                };

                // Hard-code these for now. Eventually elevate to config
                object[] propertyValues = new object[]
                {
                    true,
                    true
                };

                CustomAttributeBuilder timeoutBuilder = new CustomAttributeBuilder(
                    ctorInfo,
                    new object[] { scriptConfig.FunctionTimeout.ToString() },
                    propertyInfos,
                    propertyValues);

                customAttributes.Add(timeoutBuilder);
            }

            return customAttributes;
        }

        // TODO: DI (FACAVAL) Remove this method.
        // all restart/shutdown requests should go through the
        // IScriptHostEnvironment implementation
        internal Task RestartAsync()
        {
            _scriptHostEnvironment.RestartHost();

            return Task.CompletedTask;
        }

        internal void Shutdown()
        {
            _scriptHostEnvironment.Shutdown();
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
                    _logger.LogWarning($"Site extension version currently set to '{extensionVersion}'. " +
                        $"It is recommended that you target a major version (e.g. ~2) to avoid unintended upgrades. " +
                        $"You can change that value by updating the '{EnvironmentSettingNames.FunctionsExtensionVersion}' App Setting.");
                }
            }

            // Log whether App Insights is enabled
            if (!string.IsNullOrEmpty(_settingsManager.ApplicationInsightsInstrumentationKey))
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
            FileUtility.EnsureDirectoryExists(_hostLogPath);

            if (!_environment.FileSystemIsReadOnly())
            {
                FileUtility.EnsureDirectoryExists(ScriptOptions.RootScriptPath);
            }
        }

        /// <summary>
        /// Generate function wrappers from descriptors.
        /// </summary>
        private void GenerateFunctions(IEnumerable<Type> directTypes)
        {
            // generate Type level attributes
            var typeAttributes = CreateTypeAttributes(ScriptOptions);

            string generatingMsg = string.Format(CultureInfo.InvariantCulture, "Generating {0} job function(s)", Functions.Count);
            _logger?.LogInformation(generatingMsg);

            // generate the Type wrapper
            string typeName = string.Format(CultureInfo.InvariantCulture, "{0}.{1}", GeneratedTypeNamespace, GeneratedTypeName);
            Type functionWrapperType = FunctionGenerator.Generate(HostAssemblyName, typeName, typeAttributes, Functions);

            // configure the Type locator
            var types = new List<Type>();
            types.Add(functionWrapperType);
            types.AddRange(directTypes);

            _typeLocator.SetTypes(types);
        }

        /// <summary>
        /// Initialize function descriptors from metadata.
        /// </summary>
        internal async Task InitializeFunctionDescriptorsAsync(IEnumerable<FunctionMetadata> functionMetadata)
        {
            if (string.IsNullOrEmpty(_currentRuntimelanguage))
            {
                _logger.LogDebug("Adding Function descriptor providers for all languages.");
                _descriptorProviders.Add(new DotNetFunctionDescriptorProvider(this, ScriptOptions, _bindingProviders, _metricsLogger, _loggerFactory));
                _descriptorProviders.Add(new WorkerFunctionDescriptorProvider(this, ScriptOptions, _bindingProviders, _functionDispatcher, _loggerFactory));
            }
            else
            {
                _logger.LogDebug($"Adding Function descriptor provider for language {_currentRuntimelanguage}.");
                if (string.Equals(_currentRuntimelanguage, LanguageWorkerConstants.DotNetLanguageWorkerName, StringComparison.OrdinalIgnoreCase))
                {
                    _descriptorProviders.Add(new DotNetFunctionDescriptorProvider(this, ScriptOptions, _bindingProviders, _metricsLogger, _loggerFactory));
                }
                else
                {
                    _descriptorProviders.Add(new WorkerFunctionDescriptorProvider(this, ScriptOptions, _bindingProviders, _functionDispatcher, _loggerFactory));
                }
            }

            Collection<FunctionDescriptor> functions;
            using (_metricsLogger.LatencyEvent(MetricEventNames.HostStartupGetFunctionDescriptorsLatency))
            {
                _logger.LogDebug("Creating function descriptors.");
                functions = await GetFunctionDescriptorsAsync(functionMetadata, _descriptorProviders);
                _logger.LogDebug("Function descriptors created.");
            }

            Functions = functions;
        }

        private void InitializeWorkersAsync()
        {
            _logger.LogInformation("in InitializeWorkersAsync");
            _logger.LogInformation($"is _languageWorkerChannel null: {_languageWorkerChannel == null}");
            _logger.LogInformation($"is _workerConfigs null: {_workerConfigs == null}");
            WorkerConfig javaConfig = _workerConfigs.Where(c => c.Language.Equals("java", StringComparison.InvariantCultureIgnoreCase)).FirstOrDefault();
            _functionDispatcher = new FunctionDispatcher(EventManager, _languageWorkerChannel.RpcServer, _workerConfigs, _currentRuntimelanguage);
            _functionDispatcher.CreateWorkerState(javaConfig, _languageWorkerChannel);
            _eventSubscriptions.Add(EventManager.OfType<WorkerProcessErrorEvent>()
                .Subscribe(evt =>
                {
                    HandleHostError(evt.Exception);
                }));
        }

        internal async Task InitializeRpcServiceAsync(IRpcServer rpcService)
        {
            _rpcService = rpcService;

            using (_metricsLogger.LatencyEvent(MetricEventNames.HostStartupGrpcServerLatency))
            {
                try
                {
                    await _rpcService.StartAsync();
                }
                catch (Exception grpcInitEx)
                {
                    throw new HostInitializationException($"Failed to start Grpc Service. Check if your app is hitting connection limits.", grpcInitEx);
                }
            }
        }

        /// <summary>
        /// Clean up any old files or directories.
        /// </summary>
        private void CleanupFileSystem()
        {
            if (ScriptOptions.FileLoggingMode != FileLoggingMode.Never)
            {
                // initiate the cleanup in a background task so we don't
                // delay startup
                Task.Run(() => PurgeOldLogDirectories());
            }
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
                            string removeLogMessage = $"Deleting log directory '{logDir.FullName}'";
                            _logger.LogDebug(removeLogMessage);
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
                string errorMsg = "An error occurred while purging log files";
                _logger.LogError(0, ex, errorMsg);
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
                    bool isDirect = metadata.IsDirect;
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

                    _logger.LogInformation(msg);
                }

                return;
            }
        }

        /// <summary>
        /// Get the set of types that should be directly loaded. These have the "configurationSource" : "attributes" set.
        /// They will be indexed and invoked directly by the WebJobs SDK and skip the IL generator and invoker paths.
        /// </summary>
        private IEnumerable<Type> GetDirectTypes(IEnumerable<FunctionMetadata> functionMetadataList)
        {
            HashSet<Type> visitedTypes = new HashSet<Type>();

            foreach (var metadata in functionMetadataList)
            {
                if (!metadata.IsDirect)
                {
                    continue;
                }

                string path = metadata.ScriptFile;
                var typeName = Utility.GetFullClassName(metadata.EntryPoint);

                Assembly assembly = FunctionAssemblyLoadContext.Shared.LoadFromAssemblyPath(path);
                var type = assembly.GetType(typeName);
                if (type != null)
                {
                    visitedTypes.Add(type);
                }
                else
                {
                    // This likely means the function.json and dlls are out of sync. Perhaps a badly generated function.json?
                    string msg = $"Failed to load type '{typeName}' from '{path}'";
                    _logger.LogWarning(msg);
                }
            }
            return visitedTypes;
        }

        internal async Task<Collection<FunctionDescriptor>> GetFunctionDescriptorsAsync(IEnumerable<FunctionMetadata> functions, IEnumerable<FunctionDescriptorProvider> descriptorProviders)
        {
            Collection<FunctionDescriptor> functionDescriptors = new Collection<FunctionDescriptor>();
            var httpFunctions = new Dictionary<string, HttpTriggerAttribute>();

            if (!_scriptHostEnvironment.IsDevelopment() && !Utility.IsSingleLanguage(functions, _currentRuntimelanguage))
            {
                throw new HostInitializationException($"Found functions with more than one language. Select a language for your function app by specifying {LanguageWorkerConstants.FunctionWorkerRuntimeSettingName} AppSetting");
            }

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
                }
                catch (Exception ex)
                {
                    // log any unhandled exceptions and continue
                    Utility.AddFunctionError(FunctionErrors, metadata.Name, Utility.FlattenException(ex, includeSource: false));
                }
            }

            VerifyPrecompileStatus(functionDescriptors);

            return functionDescriptors;
        }

        internal static void ValidateFunction(FunctionDescriptor function, Dictionary<string, HttpTriggerAttribute> httpFunctions)
        {
            var httpTrigger = function.GetTriggerAttributeOrNull<HttpTriggerAttribute>();
            if (httpTrigger != null)
            {
                bool isProxy = function.Metadata != null && function.Metadata.IsProxy;

                ValidateHttpFunction(function.Name, httpTrigger, isProxy);

                if (!isProxy)
                {
                    // prevent duplicate/conflicting routes for functions
                    // proxy routes check is done in the proxy dll itself and proxies do not use routePrefix so should not check conflict with functions
                    foreach (var pair in httpFunctions)
                    {
                        if (HttpRoutesConflict(httpTrigger, pair.Value))
                        {
                            throw new InvalidOperationException($"The route specified conflicts with the route defined by function '{pair.Key}'.");
                        }
                    }
                }

                if (httpFunctions.ContainsKey(function.Name))
                {
                    throw new InvalidOperationException($"The function or proxy name '{function.Name}' must be unique within the function app.");
                }

                httpFunctions.Add(function.Name, httpTrigger);
            }
        }

        internal static void ValidateHttpFunction(string functionName, HttpTriggerAttribute httpTrigger, bool isProxy = false)
        {
            if (string.IsNullOrWhiteSpace(httpTrigger.Route) && !isProxy)
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
                throw new ArgumentNullException("exception");
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
            else if (exception is LanguageWorkerChannelException)
            {
                AddLanguageWorkerChannelErrors(_functionDispatcher, FunctionErrors);
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
                    function.Metadata.IsDisabled = metadata.IsDisabled;
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

            string message = $"Host initialized ({_stopwatch.ElapsedMilliseconds}ms)";
            _logger.LogInformation(message);

            HostInitialized?.Invoke(this, EventArgs.Empty);

            base.OnHostInitialized();
        }

        protected override void OnHostStarted()
        {
            HostStarted?.Invoke(this, EventArgs.Empty);

            base.OnHostStarted();

            string message = $"Host started ({_stopwatch.ElapsedMilliseconds}ms)";
            _logger.LogInformation(message);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                foreach (var subscription in _eventSubscriptions)
                {
                    subscription.Dispose();
                }

                _functionDispatcher?.Dispose();
                (_processRegistry as IDisposable)?.Dispose();

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
        }
    }
}
